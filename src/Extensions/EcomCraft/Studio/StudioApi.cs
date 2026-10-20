using System.Globalization;
using System.Net.Http;
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

/// <summary>Small, local-only adapter. This is not a durable task service or a platform-wide egress policy.</summary>
[API.APIClass("EcomCraft image and text-to-video workspace. Cloud providers are not configured.")]
public static class StudioApi
{
    public const string LocalScopeKey = "ecomcraft_execution_scope";
    private const int MaxReferenceBytes = 5 * 1024 * 1024;
    private const int MaxLoras = 5;
    public const string PublishedWorkflowScopeKey = "ecomcraft_published_workflow";

    private static readonly HashSet<string> WorkflowFormParameterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "negativeprompt", "model", "width", "height", "images", "steps", "cfgscale", "seed", "sampler", "scheduler"
    };

    private static readonly HashSet<string> WorkflowDeniedParameterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "comfyworkflowraw", "comfyworkflowparammetadata", "comfyuicustomworkflow", "extra_metadata", "session_id",
        "backend", "backendtype", "exactbackendid", "donotsave", "webhooks", "forwardrawbackenddata",
        "forwardswarmdata", "continueaftererrors", "nointernalSpecialHandling"
    };

    private static readonly HashSet<string> SupportedWorkflowInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "integer", "decimal", "boolean", "dropdown", "image", "model", "list"
    };

    /// <summary>Additional restriction, never an override of native backend matching or user permissions.</summary>
    public static bool ValidateLocalBackend(T2IParamInput input, BackendHandler.T2IBackendData backend)
    {
        if (!input.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start")
        {
            return true;
        }
        bool allowed = backend.Backend is ComfyUISelfStartBackend && backend.Backend.IsReal;
        if (!allowed)
        {
            input.RefusalReasons.Add("EcomCraft workspace requests only permit local self-starting ComfyUI backends.");
        }
        return allowed;
    }

    private static bool CanView(Session session) => session?.User is not null
        && session.User.HasPermission(Permissions.FundamentalGenerateTabAccess)
        && session.User.HasPermission(Permissions.FundamentalModelAccess)
        && session.User.HasPermission(Permissions.ModelParams);

    private static bool IsLocalAvailable(BackendHandler.T2IBackendData data) => data.Backend is ComfyUISelfStartBackend
        && data.Backend.IsReal && data.Backend.IsEnabled && !data.Backend.ShutDownReserve
        && data.Backend.CanLoadModels && data.Backend.MaxUsages > 0 && data.Usages < data.Backend.MaxUsages
        && data.Backend.Reservations == 0 && data.Backend.Status == BackendStatus.RUNNING;

    private static IEnumerable<T2IModel> LocalModels(Session session) =>
        Program.T2IModelSets.TryGetValue("Stable-Diffusion", out T2IModelHandler handler)
            ? handler.Models.Values.Where(m => m.IsSupportedModelType && session.User.IsAllowedModel(m.Name)) : [];

    private static List<string> LocalLoras(Session session) =>
        Program.T2IModelSets.TryGetValue("LoRA", out T2IModelHandler handler)
            ? handler.ListModelNamesFor(session).Where(n => n != "(None)").ToList() : [];

    private static JArray OptionPairs(IEnumerable<string> values)
    {
        JArray array = new();
        foreach (string value in values)
        {
            string id = value.Before("///");
            string name = value.Contains("///") ? value.After("///") : value;
            array.Add(new JObject() { ["id"] = id, ["name"] = name });
        }
        return array;
    }

    private static string Kind(T2IModel model)
    {
        T2IModelCompatClass compat = model.ModelClass?.CompatClass;
        if (compat is null || compat.IsAudioModel)
        {
            return null;
        }
        if (compat.IsText2Video)
        {
            return "video";
        }
        return compat.IsImage2Video ? null : "image";
    }

    [API.APIDescription("Read authorized local model metadata and observed local backend availability. No probes or cloud calls; capabilities still require live validation.", "models: [], local_backends_observed: 0, can_generate: false, cloud_configured: false")]
    public static Task<JObject> EcomCraftStudioCatalog(Session session)
    {
        if (!CanView(session))
        {
            return Task.FromResult(Error("无权访问创作工作台或模型列表。", "bad_permissions"));
        }
        JObject result = new()
        {
            ["user_name"] = session.User.UserID,
            ["can_generate"] = session.User.HasPermission(Permissions.BasicImageGeneration),
            ["can_generate_video"] = session.User.HasPermission(Permissions.BasicImageGeneration) && session.User.HasPermission(Permissions.ParamVideo),
            ["can_manage_settings"] = session.User.HasPermission(Permissions.EditServerSettings),
            ["local_backends_observed"] = Program.Backends.EnumerateT2IBackends.Count(IsLocalAvailable),
            ["local_backends_starting"] = Program.Backends.EnumerateT2IBackends.Count(data => data.Backend is ComfyUISelfStartBackend
                && data.Backend.IsReal && data.Backend.IsEnabled && data.Backend.Status is BackendStatus.WAITING or BackendStatus.LOADING),
            ["local_backends_errored"] = Program.Backends.EnumerateT2IBackends.Count(data => data.Backend is ComfyUISelfStartBackend
                && data.Backend.IsReal && data.Backend.IsEnabled && data.Backend.Status == BackendStatus.ERRORED),
            ["cloud_configured"] = false,
            ["models"] = new JArray(LocalModels(session).Where(m => Kind(m) is not null).OrderBy(m => m.Name).Select(m => new JObject()
            {
                ["id"] = m.Name,
                ["kind"] = Kind(m),
                ["class_name"] = m.ModelClass.Name,
                ["width"] = m.StandardWidth > 0 ? m.StandardWidth : m.ModelClass.StandardWidth,
                ["height"] = m.StandardHeight > 0 ? m.StandardHeight : m.ModelClass.StandardHeight,
                ["resolution_step"] = Math.Max(8, m.ModelClass.CompatClass.ResolutionPrecision),
                ["image_reference"] = Kind(m) == "image"
            })),
            ["loras"] = new JArray(LocalLoras(session).OrderBy(n => n)),
            ["samplers"] = OptionPairs(ComfyUIBackendExtension.Samplers),
            ["schedulers"] = OptionPairs(ComfyUIBackendExtension.Schedulers),
            ["workflows"] = PublishedWorkflowCatalog(session)
        };
        return Task.FromResult(result);
    }

    [API.APIDescription("Generate using a validated allowlist on local self-starting ComfyUI only. No cloud fallback, automatic retries or durable task guarantees. rawInput is the root request JSON.", "images: [\"View/user/result.png\"]")]
    public static async Task<JObject> EcomCraftStudioGenerate(Session session, JObject rawInput)
    {
        if (!CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Error("此账号没有生成权限。请联系管理员。", "bad_permissions");
        }
        if ((string)rawInput["source"] != "local")
        {
            return Error("云端服务尚未配置；任务未提交，也不会自动切换服务商。", "cloud_unavailable");
        }
        string requestedWorkflow = (string)rawInput["workflow_id"];
        if (!string.IsNullOrWhiteSpace(requestedWorkflow))
        {
            return await GeneratePublishedWorkflow(session, rawInput, requestedWorkflow);
        }
        string mode = (string)rawInput["mode"] ?? "text2img";
        if (mode is not ("text2img" or "img2img" or "inpaint" or "outpaint" or "upscale" or "video"))
        {
            return Error("未知的创作方式。", "invalid_mode");
        }
        string kind = (string)rawInput["kind"];
        string modelId = (string)rawInput["model"];
        T2IModel model = LocalModels(session).FirstOrDefault(m => m.Name == modelId);
        if (model is null || Kind(model) != kind || kind is not ("image" or "video"))
        {
            return Error("模型不可用、权限已变化或模型类型不匹配。请刷新模型列表。", "invalid_model");
        }
        if (kind == "video" || mode == "video")
        {
            if (mode != "video" || !session.User.HasPermission(Permissions.ParamVideo))
            {
                return Error("此账号没有视频参数权限，或视频流程选择有误。请联系管理员。", "bad_permissions");
            }
        }
        if (!Program.Backends.EnumerateT2IBackends.Any(IsLocalAvailable))
        {
            return Error("没有观测到可用的部署机 ComfyUI 节点。请由管理员检查原生引擎；不会转到远程或云端。", "local_unavailable");
        }
        string prompt = (string)rawInput["prompt"];
        string negative = (string)rawInput["negativeprompt"] ?? "";
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 12000 || negative.Length > 6000)
        {
            return Error("请填写创作描述（最多 12000 字符），排除内容最多 6000 字符。", "invalid_prompt");
        }
        // Native prompt directives may change parameters or apply presets after parsing. This adapter accepts plain text only.
        if (new[] { prompt, negative }.Any(p => p.Contains('<') || p.Contains('>') || p.Contains("__")))
        {
            return Error("本版工作台只接受普通描述，不支持 <…> 原生指令、预设或 __通配符__。高级工作流请使用原生引擎。", "unsupported_prompt_syntax");
        }
        int step = Math.Max(8, model.ModelClass.CompatClass.ResolutionPrecision);
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, kind == "video" ? 1 : 4, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–4 张，视频每批 1 条。", "invalid_parameters");
        }
        // Build fresh input: never forward caller-supplied presets, workflow JSON, routes, metadata or backend selectors.
        JObject input = new()
        {
            ["model"] = model.Name, ["prompt"] = prompt, ["negativeprompt"] = negative,
            ["width"] = width, ["height"] = height, ["images"] = images,
            ["webhooks"] = "None", ["donotsave"] = false,
            ["extra_metadata"] = new JObject() { [LocalScopeKey] = "local_self_start" }
        };
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, uint.MaxValue, true)
            || !CopyOptionalNumber(rawInput, input, "steps", 1, 100, true)
            || !CopyOptionalNumber(rawInput, input, "cfgscale", 0, 30, false))
        {
            return Error("种子、步数或提示词强度不在允许范围内。", "invalid_parameters");
        }

        // ----- Reference / edit images -----
        string reference = (string)rawInput["initimage"];
        bool needsReference = mode is "img2img" or "inpaint" or "outpaint" or "upscale";
        if (needsReference && string.IsNullOrEmpty(reference))
        {
            return Error("该创作方式需要先提供初始图。", "invalid_reference");
        }
        if (!string.IsNullOrEmpty(reference))
        {
            if (kind != "image" || !ValidReference(reference))
            {
                return Error("仅图片流程支持初始图；请使用不超过 5 MB 的 PNG、JPEG 或 WebP。", "invalid_reference");
            }
            int refWidth = 0, refHeight = 0;
            try
            {
                SwarmUI.Media.ImageFile decoded = SwarmUI.Media.ImageFile.FromDataString(reference);
                (refWidth, refHeight) = decoded.GetResolution();
                if (refWidth > 8192 || refHeight > 8192 || (long)refWidth * refHeight > 40_000_000)
                {
                    return Error("初始图尺寸过大，请缩小到最长边 8192 像素以内且不超过 4000 万像素。", "invalid_reference");
                }
            }
            catch (Exception)
            {
                return Error("无法解码初始图，请重新导出为 PNG、JPEG 或 WebP。", "invalid_reference");
            }
            input["initimage"] = reference;
            // Img2img / inpaint / outpaint need a creativity value; upscale keeps it optional.
            if (mode is "img2img" or "inpaint" or "outpaint")
            {
                if (!CopyOptionalNumber(rawInput, input, "initimagecreativity", 0, 1, false) || input["initimagecreativity"] is null)
                {
                    return Error("请提供 0–1 的重绘幅度。", "invalid_parameters");
                }
            }
        }

        // ----- Mask (inpaint / outpaint) -----
        string mask = (string)rawInput["maskimage"];
        if (mode is "inpaint" or "outpaint")
        {
            if (string.IsNullOrEmpty(mask) || !ValidReference(mask)
                || !Integer(rawInput, "maskshrinkgrow", 0, 512, out int shrink)
                || !Integer(rawInput, "maskblur", 0, 64, out int blur))
            {
                return Error("局部重绘/扩图需要有效的蒙版，蒙版收缩 0–512、羽化 0–64。", "invalid_mask");
            }
            input["maskimage"] = mask;
            input["maskshrinkgrow"] = shrink;
            input["maskblur"] = blur;
            input["initimagerecompositemask"] = true;
        }

        // ----- Upscale -----
        if (mode == "upscale")
        {
            if (!CopyOptionalNumber(rawInput, input, "refinerupscale", 1.5, 4, false) || input["refinerupscale"] is null)
            {
                return Error("请提供 1.5–4 的放大倍数。", "invalid_parameters");
            }
            // Keep the base stage close to the supplied image, then refine at the larger size.
            input["initimagecreativity"] = 0.25;
        }

        // ----- LoRAs -----
        JToken loraToken = rawInput["loras"];
        if (loraToken is not null && loraToken.Type != JTokenType.Null)
        {
            JArray loras = null;
            JArray weights = null;
            string loraError = null;
            if (loraToken.Type != JTokenType.Array || !TryBuildLoras(loraToken as JArray, rawInput["loraweights"], LocalLoras(session), out loras, out weights, out loraError))
            {
                return Error(loraError ?? "LoRA 配置无效。", "invalid_loras");
            }
            if (loras.Count > 0)
            {
                input["loras"] = loras;
                input["loraweights"] = weights;
            }
        }

        // ----- Sampler / scheduler -----
        if (!MatchOption(rawInput, "sampler", ComfyUIBackendExtension.Samplers, out string samplerError))
        {
            return Error(samplerError, "invalid_sampler");
        }
        if (!MatchOption(rawInput, "scheduler", ComfyUIBackendExtension.Schedulers, out string schedulerError))
        {
            return Error(schedulerError, "invalid_scheduler");
        }

        if (mode == "video")
        {
            if (!Integer(rawInput, "text2videoframes", 1, 129, out int frames))
            {
                return Error("视频帧数须为 1–129。引擎可能按模型要求调整帧数。", "invalid_parameters");
            }
            input["text2videoframes"] = frames;
            input["videoformat"] = "h264-mp4";
            if (!CopyOptionalNumber(rawInput, input, "videofps", 1, 60, true))
            {
                return Error("视频帧率须为 1–60，或留空以使用引擎默认值。", "invalid_parameters");
            }
        }
        try
        {
            JObject result = await T2IAPI.GenerateText2Image(session, images, input);
            if (result.ContainsKey("error"))
            {
                return Error("引擎未完整返回结果。请在原生引擎检查模型依赖、参数和个人历史；可能已有部分输出，请勿直接重复提交。", "generation_failed");
            }
            return result;
        }
        catch (Exception)
        {
            // No exception text in browser responses: upstream messages can contain paths or backend credentials.
            return Error("无法确认引擎执行结果。请在原生引擎核对队列和个人历史后再重试。", "generation_uncertain");
        }
    }

    /// <summary>Allows the stored-workflow selector only for a published workflow in an EcomCraft-local request.</summary>
    public static bool PublishedWorkflowPermissionOverride(string parameterName, string value, T2IParamInput input)
    {
        if (!string.Equals(parameterName, "comfyuicustomworkflow", StringComparison.OrdinalIgnoreCase)
            || input?.SourceSession?.User is not User user
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !input.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start"
            || !input.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested) || requested?.ToString() != value)
        {
            return false;
        }
        return TryGetPublishedWorkflow(value, out _, out _, out _);
    }

    /// <summary>Provides the saved parameter schema only for an approved workflow; arbitrary client-supplied schemas are ignored.</summary>
    public static T2IParamType PublishedWorkflowDynamicParamProvider(string name, T2IParamInput context)
    {
        if (context?.SourceSession?.User is not User user
            || user.HasPermission(ComfyUIBackendExtension.PermDynamicCustomWorkflows)
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !context.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start"
            || !context.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested)
            || !TryGetPublishedWorkflow(requested?.ToString(), out _, out JObject metadata, out JObject savedValues))
        {
            return null;
        }
        if (string.Equals(name, "comfyworkflowparammetadata", StringComparison.OrdinalIgnoreCase))
        {
            return ComfyUIBackendExtension.FakeParameterMetadata;
        }
        if (WorkflowDeniedParameterIds.Contains(name) || WorkflowFormParameterIds.Contains(name))
        {
            return null;
        }
        JArray exposedFields = PublishedWorkflowInputs(context.SourceSession, metadata, savedValues, out bool supported);
        if (!supported || !exposedFields.OfType<JObject>().Any(field =>
            string.Equals(field["id"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (metadata.TryGetValue(name, out JToken parameter) && parameter is JObject parameterObject)
        {
            return T2IParamType.FromNet(parameterObject);
        }
        return null;
    }

    private static JArray PublishedWorkflowCatalog(Session session)
    {
        JArray published = new();
        if (session?.User is null || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return published;
        }
        foreach (string name in ComfyUIBackendExtension.CustomWorkflows.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetPublishedWorkflow(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
                out JObject metadata, out JObject savedValues))
            {
                continue;
            }
            JArray fields = PublishedWorkflowInputs(session, metadata, savedValues, out bool supported);
            if (!supported)
            {
                continue;
            }
            published.Add(new JObject()
            {
                ["id"] = workflow.Name,
                ["name"] = workflow.Name,
                ["kind"] = InferWorkflowKind(workflow),
                ["description"] = workflow.Description ?? "",
                ["inputs"] = fields
            });
        }
        return published;
    }

    private static JArray PublishedWorkflowInputs(Session session, JObject metadata, JObject savedValues, out bool supported)
    {
        JArray fields = new();
        supported = true;
        foreach (JProperty property in metadata.Properties())
        {
            if (property.Value is not JObject definition || WorkflowDeniedParameterIds.Contains(property.Name)
                || WorkflowFormParameterIds.Contains(property.Name))
            {
                continue;
            }
            if (definition["visible"] is not null && bool.TryParse(definition["visible"].ToString(), out bool visible) && !visible)
            {
                continue;
            }
            string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
            if (!SupportedWorkflowInputTypes.Contains(type))
            {
                continue; // Keep fixed/hidden values from the saved template, but never expose unknown input types to the browser.
            }
            if (type == "dropdown")
            {
                if (definition["values"] is not JArray dropdownValues || dropdownValues.Count == 0)
                {
                    supported = false;
                    return fields;
                }
            }
            JToken defaultValue = savedValues[property.Name]?.DeepClone() ?? definition["default"]?.DeepClone();
            if (type == "image")
            {
                // Never disclose a workflow owner's default image data or their private output path to other users.
                defaultValue = new JValue("");
            }
            JObject field = new()
            {
                ["id"] = property.Name,
                ["name"] = definition["name"]?.ToString() ?? property.Name,
                ["type"] = type,
                ["description"] = definition["description"]?.ToString() ?? "",
                ["default"] = defaultValue,
                ["min"] = definition["min"]?.DeepClone() ?? new JValue(0),
                ["max"] = definition["max"]?.DeepClone() ?? new JValue(0),
                ["step"] = definition["step"]?.DeepClone() ?? new JValue(1),
                ["subtype"] = definition["subtype"]?.DeepClone()
            };
            if (type == "model")
            {
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler))
                {
                    List<string> allowedModels = handler.ListModelNamesFor(session);
                    if (allowedModels.Count == 0)
                    {
                        supported = false;
                        return fields;
                    }
                    field["values"] = new JArray(allowedModels);
                }
                else
                {
                    supported = false;
                    return fields;
                }
            }
            else if (definition["values"] is not null)
            {
                field["values"] = definition["values"].DeepClone();
            }
            fields.Add(field);
        }
        return fields;
    }

    private static string InferWorkflowKind(ComfyUIBackendExtension.ComfyCustomWorkflow workflow)
    {
        JObject graph = ParseWorkflowObject(workflow?.Prompt);
        bool containsVideoNode = graph?.Properties().Any(property =>
        {
            string classType = property.Value is JObject node ? node["class_type"]?.ToString() ?? "" : "";
            return classType.Contains("video", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("VHS_", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("AnimateDiff", StringComparison.OrdinalIgnoreCase);
        }) == true;
        return containsVideoNode ? "video" : "image";
    }

    private static bool TryGetPublishedWorkflow(string name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        out JObject metadata, out JObject savedValues)
    {
        workflow = null;
        metadata = null;
        savedValues = new JObject();
        if (string.IsNullOrWhiteSpace(name) || !ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name))
        {
            return false;
        }
        workflow = ComfyUIBackendExtension.GetWorkflowByName(name);
        if (workflow is null || !workflow.EnableInSimple || string.IsNullOrWhiteSpace(workflow.Prompt))
        {
            workflow = null;
            return false;
        }
        metadata = ParseWorkflowObject(workflow.CustomParams);
        savedValues = ParseWorkflowObject(workflow.ParamValues) ?? new JObject();
        if (metadata is null || ParseWorkflowObject(workflow.Prompt) is null)
        {
            workflow = null;
            return false;
        }
        return true;
    }

    private static JObject ParseWorkflowObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JToken.Parse(json) as JObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<JObject> GeneratePublishedWorkflow(Session session, JObject rawInput, string workflowId)
    {
        if (!session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return Error("此账号没有使用已发布工作流的权限。", "bad_permissions");
        }
        if (!TryGetPublishedWorkflow(workflowId, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
            out JObject metadata, out JObject savedValues))
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
        string modelId = (string)rawInput["model"];
        T2IModel model = LocalModels(session).FirstOrDefault(m => m.Name == modelId && Kind(m) == kind);
        if (model is null)
        {
            return Error("模型不可用、账号无权访问，或模型类型与当前创作类型不匹配。", "invalid_model");
        }
        if (!Program.Backends.EnumerateT2IBackends.Any(IsLocalAvailable))
        {
            return Error("没有可用的本机自启动 ComfyUI 节点；不会转发到远程或云端。", "local_unavailable");
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
        int step = Math.Max(8, model.ModelClass.CompatClass.ResolutionPrecision);
        int imageLimit = kind == "video" ? 1 : 4;
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, imageLimit, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–4 张，视频每批 1 条。", "invalid_parameters");
        }
        JToken rawWorkflowParams = rawInput["workflow_params"];
        if (rawWorkflowParams is not null && rawWorkflowParams.Type is not (JTokenType.Object or JTokenType.Null))
        {
            return Error("工作流参数格式无效。", "invalid_workflow_parameters");
        }
        JObject requestedParams = rawWorkflowParams as JObject ?? new JObject();
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
            ["comfyuicustomworkflow"] = workflow.Name
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
        input["comfyworkflowparammetadata"] = workflow.CustomParams;
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
        input["model"] = model.Name;
        input["prompt"] = prompt;
        input["negativeprompt"] = negative;
        input["width"] = width;
        input["height"] = height;
        input["images"] = images;
        input["webhooks"] = "None";
        input["donotsave"] = false;
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, uint.MaxValue, true)
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
            [LocalScopeKey] = "local_self_start",
            [PublishedWorkflowScopeKey] = workflow.Name
        };
        try
        {
            JObject result = await T2IAPI.GenerateText2Image(session, images, input);
            if (result.ContainsKey("error"))
            {
                return Error("引擎未完整返回结果。请在原生引擎检查工作流依赖、参数和个人历史；可能已有部分输出，请勿直接重复提交。", "generation_failed");
            }
            return result;
        }
        catch (Exception)
        {
            return Error("无法确认工作流执行结果。请在原生引擎核对队列和个人历史后再重试。", "generation_uncertain");
        }
    }

    private static bool ValidatePublishedWorkflowInput(Session session, string id, JToken value, JObject definition, out string error)
    {
        error = null;
        string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
        if (value.Type == JTokenType.Null)
        {
            return true;
        }
        string raw = value.Type == JTokenType.String ? value.Value<string>() ?? "" : value.ToString();
        double min = ReadWorkflowBound(definition["min"]);
        double max = ReadWorkflowBound(definition["max"]);
        bool hasBounds = min != 0 || max != 0;
        switch (type)
        {
            case "text":
                if (value.Type != JTokenType.String || raw.Length > 12000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流参数“{id}”必须是普通文本，不能超过 12000 个字符，也不能包含原生指令语法。";
                    return false;
                }
                return true;
            case "integer":
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integerValue)
                    || (hasBounds && (integerValue < min || integerValue > max)))
                {
                    error = $"工作流参数“{id}”的整数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "decimal":
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalValue)
                    || !double.IsFinite(decimalValue) || (hasBounds && (decimalValue < min || decimalValue > max)))
                {
                    error = $"工作流参数“{id}”的数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "boolean":
                if (!bool.TryParse(raw, out _))
                {
                    error = $"工作流参数“{id}”必须是布尔值。";
                    return false;
                }
                return true;
            case "dropdown":
                if (value.Type != JTokenType.String || definition["values"] is not JArray choices || choices.Count == 0
                    || !choices.Any(choice => choice.ToString() == raw || choice.ToString().Before("///") == raw))
                {
                    error = $"工作流参数“{id}”不是当前工作流允许的选项。";
                    return false;
                }
                return true;
            case "model":
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (value.Type != JTokenType.String || !Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler)
                    || T2IParamTypes.GetBestModelInList(raw, handler.ListModelNamesFor(session)) is null)
                {
                    error = $"工作流参数“{id}”引用了不存在或当前账号无权访问的模型。";
                    return false;
                }
                return true;
            case "image":
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return true;
                }
                if (raw.StartsWith("inputs/", StringComparison.Ordinal) || raw.StartsWith("raw/", StringComparison.Ordinal)
                    || raw.StartsWith("Starred/", StringComparison.Ordinal))
                {
                    return true; // SwarmUI validates and resolves these within the user's own output directory.
                }
                if (!ValidReference(raw))
                {
                    error = $"工作流图片参数“{id}”必须是 PNG、JPEG 或 WebP，且不超过 5 MB。";
                    return false;
                }
                try
                {
                    SwarmUI.Media.ImageFile decoded = SwarmUI.Media.ImageFile.FromDataString(raw);
                    (int imageWidth, int imageHeight) = decoded.GetResolution();
                    if (imageWidth > 8192 || imageHeight > 8192 || (long)imageWidth * imageHeight > 40_000_000)
                    {
                        error = $"工作流图片参数“{id}”尺寸过大。";
                        return false;
                    }
                }
                catch (Exception)
                {
                    error = $"无法解码工作流图片参数“{id}”。";
                    return false;
                }
                return true;
            case "list":
                if (value.Type != JTokenType.String || raw.Length > 4000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流列表参数“{id}”格式无效，或包含不支持的原生指令语法。";
                    return false;
                }
                return true;
            default:
                error = $"工作流参数“{id}”的数据类型“{type}”暂不支持由网页修改，请调整工作流的默认值或输入类型。";
                return false;
        }
    }

    private static double ReadWorkflowBound(JToken token)
    {
        return token is not null && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) ? value : 0;
    }

    private static bool TryBuildLoras(JArray loraArray, JToken weightToken, List<string> allowed, out JArray loras, out JArray weights, out string error)
    {
        loras = new JArray();
        weights = new JArray();
        error = null;
        if (loraArray.Count > MaxLoras)
        {
            error = $"LoRA 最多 {MaxLoras} 个。";
            return false;
        }
        double[] providedWeights = new double[loraArray.Count];
        if (weightToken is not null && weightToken.Type != JTokenType.Null)
        {
            if (weightToken.Type != JTokenType.Array || weightToken.Count() != loraArray.Count)
            {
                error = "LoRA 权重数量必须与 LoRA 数量一致。";
                return false;
            }
            for (int i = 0; i < loraArray.Count; i++)
            {
                if (!double.TryParse(weightToken[i]?.ToString(), out double w) || !double.IsFinite(w) || w < -10 || w > 10)
                {
                    error = "LoRA 权重须为 -10 到 10 的数字。";
                    return false;
                }
                providedWeights[i] = w;
            }
        }
        HashSet<string> seen = new();
        for (int i = 0; i < loraArray.Count; i++)
        {
            string name = loraArray[i]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || !allowed.Contains(name) || !seen.Add(name))
            {
                error = "LoRA 不存在、无权限或重复选择，请刷新后重试。";
                return false;
            }
            loras.Add(name);
            weights.Add(weightToken is null ? "1" : providedWeights[i].ToString());
        }
        return true;
    }

    private static bool MatchOption(JObject source, string key, List<string> allowed, out string error)
    {
        error = null;
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null || token.ToString() == "")
        {
            return true;
        }
        string value = token.ToString();
        HashSet<string> ids = new(allowed.Select(v => v.Before("///")));
        if (!ids.Contains(value))
        {
            error = $"{key} 取值不受支持，请使用列表中的选项。";
            return false;
        }
        return true;
    }

    private static bool Integer(JObject input, string key, int min, int max, out int value)
    {
        value = 0;
        return input[key]?.Type == JTokenType.Integer && int.TryParse(input[key].ToString(), out value) && value >= min && value <= max;
    }

    private static bool CopyOptionalNumber(JObject source, JObject target, string key, double min, double max, bool integral)
    {
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null)
        {
            return true;
        }
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            return false;
        }
        double value = token.Value<double>();
        if (!double.IsFinite(value) || value < min || value > max || (integral && value != Math.Truncate(value)))
        {
            return false;
        }
        target[key] = token.DeepClone();
        return true;
    }

    private static bool ValidReference(string data)
    {
        int comma = data.IndexOf(',');
        if (comma < 0 || comma > 30 || data.Length > (MaxReferenceBytes + 2) / 3 * 4 + 40
            || data[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64"))
        {
            return false;
        }
        byte[] buffer = new byte[(data.Length - comma) * 3 / 4];
        return Convert.TryFromBase64String(data[(comma + 1)..], buffer, out int count) && count > 0 && count <= MaxReferenceBytes;
    }

    private static JObject Error(string text, string id) => new() { ["error"] = text, ["error_id"] = id };
}
