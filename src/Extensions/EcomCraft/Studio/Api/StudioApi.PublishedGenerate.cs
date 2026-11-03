using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>The published-workflow generate path. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    /// <summary>Validated pieces of one published-workflow request, gathered by <see cref="ValidatePublishedHeader"/> before the input object is built (R09).</summary>
    private sealed class PublishedPlan
    {
        /// <summary>Image or video.</summary>
        public string Kind;

        /// <summary>Whether the workflow runs native (review-published graph) instead of a plain model.</summary>
        public bool Native;

        /// <summary>The chosen model; null for native workflows.</summary>
        public T2IModel Model;

        /// <summary>The creation description.</summary>
        public string Prompt;

        /// <summary>The excluded content.</summary>
        public string Negative;

        /// <summary>Generation width.</summary>
        public int Width;

        /// <summary>Generation height.</summary>
        public int Height;

        /// <summary>Images per batch.</summary>
        public int Images;

        /// <summary>The resolution step the engine requires.</summary>
        public int Step;
    }

    private static async Task<JObject> GeneratePublishedWorkflow(Session session, JObject rawInput, string workflowId, int? pinnedServer, string templateId = null,
        Func<JObject, Task> postProcess = null, GenerateOptions options = null)
    {
        if (!session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return Error("此账号没有使用已发布工作流的权限。", "bad_permissions");
        }
        // ComfyUI backend refreshes clear the in-memory workflow registry, so restore published snapshots before lookup.
        WorkflowReview.Restore();
        if (!WorkflowReview.NotOthersOwn(workflowId, session.User.UserID)
            || !TryGetPublishedWorkflow(workflowId, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow, out JObject metadata, out JObject savedValues))
        {
            return Error("工作流不存在、未发布到简单工作台，或工作流配置无效。请刷新工作流列表。", "invalid_workflow");
        }
        // Work on a copy from here on: the steps below fill defaults and the engine seed into the request.
        rawInput = (JObject)rawInput.DeepClone();
        PublishedPlan plan = new();
        JObject headerError = ValidatePublishedHeader(session, rawInput, workflow, metadata, savedValues, plan, options);
        if (headerError is not null)
        {
            return headerError;
        }
        JObject paramsError = MergePublishedParams(session, rawInput, workflow, metadata, savedValues, plan, out JObject input, options);
        if (paramsError is not null)
        {
            return paramsError;
        }
        JObject finalizeError = FinalizePublishedRequest(session, rawInput, input, workflow, metadata, plan, templateId, postProcess, options);
        if (finalizeError is not null)
        {
            return finalizeError;
        }
        // Dependencies are checked per server against the final request, so the scheduler only considers servers that can run it (Rule 1).
        JObject executable = plan.Native ? ParseWorkflowObject(workflow.Prompt) : null;
        JObject checkedInput = (JObject)input.DeepClone();
        List<string> capability(BackendHandler.T2IBackendData data)
        {
            if (!plan.Native)
            {
                return StoredWorkflowProblems(workflow, data);
            }
            if (NativeWorkflowParameters.Supports(executable, metadata, checkedInput, ExecutionServers.Schema(data), out List<string> problems))
            {
                return [];
            }
            return problems.Count > 0 ? problems : ["缺少所需节点或模型"];
        }
        return await ScheduleAndSubmit(session, plan.Images, input, pinnedServer, true, rawInput.Value<bool?>("async") is true, capability, postProcess, candidates: templateId != null);
    }

    /// <summary>The request-wide checks of a published-workflow generation: kind, the workflow's own defaults, the model, the prompt and the
    /// size; fills <paramref name="plan"/>. The check order and error texts are part of the API contract. Returns the error, or null when accepted.</summary>
    private static JObject ValidatePublishedHeader(Session session, JObject rawInput, ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        JObject metadata, JObject savedValues, PublishedPlan plan, GenerateOptions options = null)
    {
        string kind = (string)rawInput["kind"];
        if (kind is not ("image" or "video"))
        {
            return Error("未知的创作类型。", "invalid_mode");
        }
        if (kind == "video" && !session.User.HasPermission(Permissions.ParamVideo))
        {
            return Error("此账号没有视频生成权限。", "bad_permissions");
        }
        if (kind != InferWorkflowKind(workflow))
        {
            return Error("所选工作流与当前创作类型不匹配，请切换图片/视频类型或选择其他工作流。", "invalid_mode");
        }
        bool native = WorkflowReview.IsUsableBy(workflow.Name, session.User.UserID);
        if (native)
        {
            JObject defaults = NativeWorkflowParameters.FormDefaults(metadata, savedValues);
            foreach (JProperty property in defaults.Properties())
            {
                JProperty binding = metadata.Properties().FirstOrDefault(parameter => parameter.Value["form"]?.ToString() == property.Name);
                rawInput[property.Name] ??= (binding is null ? null : (rawInput["workflow_params"] as JObject)?[binding.Name])?.DeepClone()
                    ?? property.Value.DeepClone();
            }
        }
        string modelId = (string)rawInput["model"];
        T2IModel model = native ? null : LocalModels(session).FirstOrDefault(m => m.Name == modelId && Kind(m) == kind);
        if (!native && model is null)
        {
            return Error("模型不可用、账号无权访问，或模型类型与当前创作类型不匹配。", "invalid_model");
        }
        string prompt = (string)rawInput["prompt"];
        string negative = (string)rawInput["negativeprompt"] ?? "";
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 12000 || negative.Length > 6000)
        {
            return Error("请填写创作描述（最多 12000 字符），排除内容最多 6000 字符。", "invalid_prompt");
        }
        // Native prompt directives may change parameters or apply presets after parsing. This adapter accepts plain text only, with one server-side
        // exception: the reference-edit trial's own prompt uses the official <imageN> tags of the edit model (F84); anything but those exact tags
        // is still refused, and the flag itself never comes from a request field.
        string tagStripped = options?.AllowImageTagPrompt == true
            ? Regex.Replace(prompt, "<image(?:[1-9]|10)>", "", RegexOptions.CultureInvariant) : prompt;
        if (new[] { tagStripped, negative }.Any(p => p.Contains('<') || p.Contains('>') || p.Contains("__")))
        {
            return Error("本版工作台只接受普通描述，不支持原生 <…> 指令、预设或 __通配符__。", "unsupported_prompt_syntax");
        }
        int step = Math.Max(8, model?.ModelClass.CompatClass.ResolutionPrecision ?? 8);
        // The workflow path serves the simple mode's candidate count, widened to 20 (F55); the standard path keeps 1–4.
        int imageLimit = kind == "video" ? 1 : 20;
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, imageLimit, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–20 张，视频每批 1 条。", "invalid_parameters");
        }
        plan.Kind = kind;
        plan.Native = native;
        plan.Model = model;
        plan.Prompt = prompt;
        plan.Negative = negative;
        plan.Width = width;
        plan.Height = height;
        plan.Images = images;
        plan.Step = step;
        return null;
    }

    /// <summary>Merges the workflow's own saved values and the request's parameters into one allowlisted <paramref name="input"/> object;
    /// every parameter is validated against the published definition. The server-side options (<paramref name="options"/>) may add parameters of its own:
    /// they are validated the same way but never pass the page allowlist, because no page can list them (F84). Returns the error, or null when accepted.</summary>
    private static JObject MergePublishedParams(Session session, JObject rawInput, ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        JObject metadata, JObject savedValues, PublishedPlan plan, out JObject input, GenerateOptions options = null)
    {
        JToken rawWorkflowParams = rawInput["workflow_params"];
        if (rawWorkflowParams is not null && rawWorkflowParams.Type is not (JTokenType.Object or JTokenType.Null))
        {
            input = null;
            return Error("工作流参数格式无效。", "invalid_workflow_parameters");
        }
        JObject requestedParams = rawWorkflowParams as JObject ?? new JObject();
        if (plan.Native)
        {
            foreach (JProperty property in metadata.Properties())
            {
                string form = property.Value["form"]?.ToString();
                // Parameters a reviewer hid keep their snapshot value even when the page sends the matching field.
                if (!string.IsNullOrEmpty(form) && property.Value.Value<bool?>("visible") != false && rawInput[form] is JToken formValue)
                {
                    requestedParams[property.Name] = formValue.DeepClone();
                }
            }
        }
        if (requestedParams.Count > 60)
        {
            input = null;
            return Error("工作流参数数量超过限制。", "invalid_workflow_parameters");
        }
        JArray publishedFields = PublishedWorkflowInputs(session, metadata, savedValues, out bool workflowInputsSupported);
        if (!workflowInputsSupported)
        {
            input = null;
            return Error("该工作流包含暂不支持的网页输入定义，请在 SwarmUI 工作流编辑器中修正后重新发布。", "invalid_workflow_parameters");
        }
        HashSet<string> allowedWorkflowInputIds = new(publishedFields.OfType<JObject>()
            .Select(field => field["id"]?.ToString()).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        input = new JObject()
        {
            // This must precede dynamic input IDs so SwarmUI recognizes them as parameters of this stored workflow.
            ["comfyuicustomworkflow"] = workflow.Name,
            ["comfyworkflowparammetadata"] = workflow.CustomParams
        };
        foreach (JProperty property in savedValues.Properties())
        {
            if (!metadata.TryGetValue(property.Name, out JToken savedDefinition)
                || savedDefinition is not JObject savedDefinitionObject
                || WorkflowDeniedParameterIds.Contains(property.Name))
            {
                continue;
            }
            JToken value = property.Value;
            string type = savedDefinitionObject["type"]?.ToString()?.ToLowerInvariant() ?? "";
            // A published workflow may contain an owner's local image path or image data. It is a template,
            // not a shared asset, so require each user to provide image inputs explicitly.
            if (value.Type == JTokenType.Null || type == "image")
            {
                continue;
            }
            if (SupportedWorkflowInputTypes.Contains(type)
                && !ValidatePublishedWorkflowInput(session, property.Name, value, savedDefinitionObject, out _))
            {
                continue;
            }
            input[property.Name] = value.DeepClone();
        }
        foreach (JProperty property in requestedParams.Properties())
        {
            if (!allowedWorkflowInputIds.Contains(property.Name)
                || WorkflowFormParameterIds.Contains(property.Name) || WorkflowDeniedParameterIds.Contains(property.Name)
                || !metadata.TryGetValue(property.Name, out JToken definition) || definition is not JObject definitionObject)
            {
                input = null;
                return Error($"工作流参数“{property.Name}”不在此工作流的允许输入列表中。", "invalid_workflow_parameters");
            }
            if (!ValidatePublishedWorkflowInput(session, property.Name, property.Value, definitionObject, out string validationError, ImageTagsAllowed(options, definitionObject)))
            {
                input = null;
                return Error(validationError, "invalid_workflow_parameters");
            }
            string type = definitionObject["type"]?.ToString()?.ToLowerInvariant() ?? "";
            if (type == "image" && string.IsNullOrWhiteSpace(property.Value.ToString()))
            {
                input.Remove(property.Name);
            }
            else
            {
                input[property.Name] = property.Value.DeepClone();
            }
        }
        foreach (JProperty property in options?.ServerParams?.Properties() ?? Enumerable.Empty<JProperty>())
        {
            // Server-set parameters (the edit trial's images and pixel budget, F84): validated like any other value, but the page allowlist never
            // contains them, so only this server-side path can deliver them.
            if (!metadata.TryGetValue(property.Name, out JToken definition) || definition is not JObject definitionObject)
            {
                input = null;
                return Error("服务端填写的工作流参数不在工作流定义中。", "invalid_workflow_parameters");
            }
            if (!ValidatePublishedWorkflowInput(session, property.Name, property.Value, definitionObject, out string serverError))
            {
                input = null;
                return Error(serverError, "invalid_workflow_parameters");
            }
            if (definitionObject["type"]?.ToString()?.ToLowerInvariant() == "image" && string.IsNullOrWhiteSpace(property.Value.ToString()))
            {
                input.Remove(property.Name);
            }
            else
            {
                input[property.Name] = property.Value.DeepClone();
            }
        }
        return null;
    }

    /// <summary>Whether the request allows the official <c>&lt;imageN&gt;</c> tags in this parameter's text: only the server-set edit prompt
    /// (bound to the "prompt" page field) carries them (F84).</summary>
    private static bool ImageTagsAllowed(GenerateOptions options, JObject definition) =>
        options?.AllowImageTagPrompt == true && definition?["form"]?.ToString() == "prompt";

    /// <summary>Builds the root generation request from a strict allowlist; never forwards arbitrary client-supplied routes, metadata or workflow JSON.
    /// Returns the error, or null when accepted.</summary>
    private static JObject FinalizePublishedRequest(Session session, JObject rawInput, JObject input, ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        JObject metadata, PublishedPlan plan, string templateId, Func<JObject, Task> postProcess, GenerateOptions options = null)
    {
        if (!plan.Native)
        {
            input["model"] = plan.Model.Name;
        }
        else
        {
            foreach (JProperty property in metadata.Properties())
            {
                JObject definition = (JObject)property.Value;
                if (input[property.Name] is null || input[property.Name].Type == JTokenType.Null
                    || !ValidatePublishedWorkflowInput(session, property.Name, input[property.Name], definition, out _, ImageTagsAllowed(options, definition)))
                {
                    return Error("工作流参数缺失或无效，请刷新列表后重新选择工作流。", "invalid_workflow_parameters");
                }
                if (definition.Value<bool?>("native_seed") == true)
                {
                    bool random = input[property.Name].ToString() == "-1";
                    if (random)
                    {
                        input[property.Name] = Random.Shared.NextInt64(0, 9007199254740991);
                    }
                    // The engine's per-image seed drives every native seed; a hidden seed sets it so the reviewer's fixed value holds.
                    if (definition["form"]?.ToString() == "seed" && (random || definition.Value<bool?>("visible") == false))
                    {
                        rawInput["seed"] = input[property.Name].DeepClone();
                    }
                }
            }
            // Request-embedded image inputs (A34: the standard workflows' product canvas and the edit trial's photos) no longer pass through the backend's
            // upload path, which stretched them to the generation size first — reproduce that stretch here, from the same width and height, so the graphs
            // see exactly the pixels they saw before. Only this server's own data URLs are touched; user output references resolve later, as always.
            foreach (JProperty property in metadata.Properties())
            {
                if (property.Value is not JObject embedDefinition || embedDefinition["type"]?.ToString()?.ToLowerInvariant() != "image"
                    || embedDefinition.Value<bool?>("image_always_b64") != true
                    || input[property.Name] is not JValue embedValue || embedValue.Type != JTokenType.String)
                {
                    continue;
                }
                string imageData = embedValue.Value<string>();
                if (!imageData.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    input[property.Name] = SwarmUI.Media.ImageFile.FromDataString(imageData).Resize(plan.Width, plan.Height).AsDataString();
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or SixLabors.ImageSharp.UnknownImageFormatException
                    or SixLabors.ImageSharp.InvalidImageContentException or NotSupportedException or IOException)
                {
                    return Error("工作流参数缺失或无效，请刷新列表后重新选择工作流。", "invalid_workflow_parameters");
                }
            }
        }
        if (plan.Native && StandardWorkflows.NeedsProductCanvas(workflow.Name) && !StandardCanvasUsable(input[StandardWorkflows.ProductImageParam]?.ToString()))
        {
            return Error("标准工作流只重绘商品周围的场景，商品图需要是透明背景的商品画布。请通过“场景图”或“细节图”模板出图，或上传已抠图的透明 PNG。", "invalid_reference");
        }
        input["prompt"] = plan.Prompt;
        input["negativeprompt"] = plan.Negative;
        input["width"] = plan.Width;
        input["height"] = plan.Height;
        input["images"] = plan.Images;
        input["webhooks"] = "None";
        input["donotsave"] = false;
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, plan.Native ? 9007199254740991 : uint.MaxValue, true)
            || !CopyOptionalNumber(rawInput, input, "steps", 1, 100, true)
            || !CopyOptionalNumber(rawInput, input, "cfgscale", 0, 30, false))
        {
            return Error("种子、步数或提示词强度不在允许范围内。", "invalid_parameters");
        }
        if (!MatchOption(rawInput, "sampler", ComfyUIBackendExtension.Samplers, out string samplerError))
        {
            return Error(samplerError, "invalid_sampler");
        }
        if (!MatchOption(rawInput, "scheduler", ComfyUIBackendExtension.Schedulers, out string schedulerError))
        {
            return Error(schedulerError, "invalid_scheduler");
        }
        if (plan.Kind == "video")
        {
            if (!Integer(rawInput, "text2videoframes", 1, 129, out int frames))
            {
                return Error("视频帧数须为 1–129。", "invalid_parameters");
            }
            input["text2videoframes"] = frames;
            input["videoformat"] = "h264-mp4";
            if (!CopyOptionalNumber(rawInput, input, "videofps", 1, 60, true))
            {
                return Error("视频帧率须为 1–60，或留空以使用引擎默认值。", "invalid_parameters");
            }
        }
        input["extra_metadata"] = new JObject()
        {
            [ExecutionScopeKey] = ExecutionScopeValue,
            [PublishedWorkflowScopeKey] = workflow.Name
        };
        if (templateId is not null)
        {
            ((JObject)input["extra_metadata"])["ecomcraft_template"] = templateId;
        }
        if (postProcess is not null)
        {
            // Marks the work as a composite of the real product and a generated picture, for the provenance metadata written on export (F22).
            ((JObject)input["extra_metadata"])["ecomcraft_preserve"] = "true";
        }
        return null;
    }
}
