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
    private static async Task<JObject> GeneratePublishedWorkflow(Session session, JObject rawInput, string workflowId, int? pinnedServer, string templateId = null,
        Func<JObject, Task> postProcess = null)
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
        rawInput = (JObject)rawInput.DeepClone();
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
        if (new[] { prompt, negative }.Any(p => p.Contains('<') || p.Contains('>') || p.Contains("__")))
        {
            return Error("本版工作台只接受普通描述，不支持原生 <…> 指令、预设或 __通配符__。", "unsupported_prompt_syntax");
        }
        int step = Math.Max(8, model?.ModelClass.CompatClass.ResolutionPrecision ?? 8);
        // The workflow path serves the simple mode's candidate count, widened to 20 (F55); the standard path below keeps 1–4.
        int imageLimit = kind == "video" ? 1 : 20;
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, imageLimit, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–20 张，视频每批 1 条。", "invalid_parameters");
        }
        JToken rawWorkflowParams = rawInput["workflow_params"];
        if (rawWorkflowParams is not null && rawWorkflowParams.Type is not (JTokenType.Object or JTokenType.Null))
        {
            return Error("工作流参数格式无效。", "invalid_workflow_parameters");
        }
        JObject requestedParams = rawWorkflowParams as JObject ?? new JObject();
        if (native)
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
            return Error("工作流参数数量超过限制。", "invalid_workflow_parameters");
        }
        JArray publishedFields = PublishedWorkflowInputs(session, metadata, savedValues, out bool workflowInputsSupported);
        if (!workflowInputsSupported)
        {
            return Error("该工作流包含暂不支持的网页输入定义，请在 SwarmUI 工作流编辑器中修正后重新发布。", "invalid_workflow_parameters");
        }
        HashSet<string> allowedWorkflowInputIds = new(publishedFields.OfType<JObject>()
            .Select(field => field["id"]?.ToString()).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        JObject input = new()
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
                return Error($"工作流参数“{property.Name}”不在此工作流的允许输入列表中。", "invalid_workflow_parameters");
            }
            if (!ValidatePublishedWorkflowInput(session, property.Name, property.Value, definitionObject, out string validationError))
            {
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
        // Build the root request from a strict allowlist; never forward arbitrary client-supplied routes, metadata or workflow JSON.
        if (!native)
        {
            input["model"] = model.Name;
        }
        else
        {
            foreach (JProperty property in metadata.Properties())
            {
                JObject definition = (JObject)property.Value;
                if (input[property.Name] is null || input[property.Name].Type == JTokenType.Null
                    || !ValidatePublishedWorkflowInput(session, property.Name, input[property.Name], definition, out _))
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
        }
        if (native && StandardWorkflows.IsStandard(workflow.Name) && !StandardCanvasUsable(input[StandardWorkflows.ProductImageParam]?.ToString()))
        {
            return Error("标准工作流只重绘商品周围的场景，商品图需要是透明背景的商品画布。请通过“场景图”或“细节图”模板出图，或上传已抠图的透明 PNG。", "invalid_reference");
        }
        input["prompt"] = prompt;
        input["negativeprompt"] = negative;
        input["width"] = width;
        input["height"] = height;
        input["images"] = images;
        input["webhooks"] = "None";
        input["donotsave"] = false;
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, native ? 9007199254740991 : uint.MaxValue, true)
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
        if (kind == "video")
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
        // Dependencies are checked per server against the final request, so the scheduler only considers servers that can run it (Rule 1).
        JObject executable = native ? ParseWorkflowObject(workflow.Prompt) : null;
        JObject checkedInput = (JObject)input.DeepClone();
        List<string> capability(BackendHandler.T2IBackendData data)
        {
            if (!native)
            {
                return StoredWorkflowProblems(workflow, data);
            }
            if (NativeWorkflowParameters.Supports(executable, metadata, checkedInput, ExecutionServers.Schema(data), out List<string> problems))
            {
                return [];
            }
            return problems.Count > 0 ? problems : ["缺少所需节点或模型"];
        }
        return await ScheduleAndSubmit(session, images, input, pinnedServer, true, rawInput.Value<bool?>("async") is true, capability, postProcess, candidates: templateId != null);
    }
}
