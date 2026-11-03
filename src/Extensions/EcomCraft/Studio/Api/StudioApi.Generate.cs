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


/// <summary>The studio generate API route and its prompt helpers. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    [API.APIDescription("Generate using a validated allowlist on registered ComfyUI execution servers only. No cloud fallback, automatic retries or durable task guarantees. rawInput is the root request JSON.", "images: [\"View/user/result.png\"]")]
    public static Task<JObject> EcomCraftStudioGenerate(Session session, JObject rawInput)
    {
        if (!CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Task.FromResult(Error("此账号没有生成权限。请联系管理员。", "bad_permissions"));
        }
        // The direct-fusion trial's template (F84) is driven only by the batch's server-side options: its images, prompt tags and workflow choice never
        // come from a request, so a direct call would reach the engine without its inputs (T123fix). Reject it here with the reason; the internal
        // path (GenerateInternal) keeps serving the batch, whose server-written options are untouched.
        if (rawInput["template_id"]?.Type == JTokenType.String && rawInput.Value<string>("template_id") == "reference-edit")
        {
            return Task.FromResult(Error("直接融合（试用）只能在“照参考图做”里开启开关使用，不能单独提交。", "invalid_template"));
        }
        return GenerateInternal(session, rawInput, new GenerateOptions());
    }

    /// <summary>The generate route's implementation, shared with server-side callers (R08): internal flags such as a raw prompt travel in
    /// <paramref name="options"/>, so no client-visible request field can set them.</summary>
    internal static async Task<JObject> GenerateInternal(Session session, JObject rawInput, GenerateOptions options)
    {
        if (!CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Error("此账号没有生成权限。请联系管理员。", "bad_permissions");
        }
        // A critically full disk rejects new work up front (F46); saves would fail midway otherwise.
        string diskFull = DiskMonitor.CriticalText();
        if (diskFull is not null)
        {
            return Error(diskFull, "disk_full");
        }
        if ((string)rawInput["source"] != "local")
        {
            return Error("云端服务尚未配置；任务未提交，也不会自动切换服务商。", "cloud_unavailable");
        }
        if (!TryReadPinnedServer(rawInput, out int? pinnedServer))
        {
            return Error("所选执行服务器不存在或已被停用，请刷新后重新选择。", "invalid_execution_server");
        }
        string requestedTemplate = rawInput["template_id"]?.Type == JTokenType.String ? rawInput.Value<string>("template_id") : null;
        if (!string.IsNullOrWhiteSpace(requestedTemplate))
        {
            // Without a prompt, a style and one-line description (or the bilingual prompt panel, F54) are turned into the prompt here,
            // so the model's answer can only become prompt text (F37).
            JObject templateInput = rawInput;
            PromptService.Composed composed = null;
            // The output size is chosen before generating: given as { width, height }, or by default the platform's slot size for the template's image kind.
            (int Width, int Height)? outputSize = null;
            if (rawInput["output_size"] is JObject)
            {
                if (!OutputSizeRules.TryParse(rawInput["output_size"], out int outputWidth, out int outputHeight))
                {
                    return Error($"出图尺寸须为 {OutputSizeRules.MinSide}–{OutputSizeRules.MaxSide} 像素的整数。", "invalid_parameters");
                }
                outputSize = (outputWidth, outputHeight);
            }
            else
            {
                outputSize = Export.ExportService.SlotSize(rawInput["platform"]?.Type == JTokenType.String ? rawInput.Value<string>("platform") : "",
                    TemplateStore.Find(requestedTemplate)?.Template?.Kind);
            }
            // Template generations (the simple mode) always run near one megapixel (T114, user decision 2026-11-03); the professional mode picks its own
            // width and height, starting from the recipe's per-tier recommendation.
            (int Width, int Height)? generationSize = outputSize is (int sizeWidth, int sizeHeight)
                ? OutputSizeRules.GenerationSize(sizeWidth, sizeHeight, OutputSizeRules.DefaultGenerationPixels) : null;
            string description = rawInput["description"]?.Type == JTokenType.String ? rawInput.Value<string>("description").Trim() : "";
            string styleId = rawInput["style_id"]?.Type == JTokenType.String ? rawInput.Value<string>("style_id") : "";
            string productTypeId = rawInput["product_type_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_type_id") : "";
            bool needModel = rawInput["need_model"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("need_model");
            string promptEn = rawInput["prompt_en"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_en") : "";
            string promptZh = rawInput["prompt_zh"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_zh") : "";
            if (!PromptService.KnownProductType(productTypeId))
            {
                return Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
            }
            if (description.Length > PromptService.MaxDescriptionLength)
            {
                return Error($"描述最多 {PromptService.MaxDescriptionLength} 个字符。", "invalid_prompt");
            }
            if (promptEn.Length > PromptRules.MaxPromptLength || promptZh.Length > PromptRules.MaxPromptLength)
            {
                return Error($"提示词最多 {PromptRules.MaxPromptLength} 个字符。", "invalid_prompt");
            }
            // A brand kit (F12) adds its color words and preferred background to the prompt; the client only sends the kit's ID.
            string brandFragment = "";
            if (rawInput["brand_kit_id"]?.Type == JTokenType.String && rawInput.Value<string>("brand_kit_id").Length > 0)
            {
                (string fragment, string problem) = Brand.BrandKitLibrary.FragmentFor(session.User.UserID, rawInput.Value<string>("brand_kit_id"));
                if (problem is not null)
                {
                    return Error(problem, "invalid_brand_kit");
                }
                brandFragment = fragment;
            }
            // Main images for platforms such as Amazon skip generation entirely (F22), so they never wait for the language model.
            JObject cutoutOnly = await TemplateApi.TryCutoutOnlyAsync(session, rawInput, requestedTemplate, outputSize);
            if (cutoutOnly is not null)
            {
                return cutoutOnly;
            }
            bool compose = string.IsNullOrWhiteSpace(rawInput["prompt"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt") : null)
                && (description.Length > 0 || styleId.Length > 0 || productTypeId.Length > 0 || needModel || PromptRules.PanelSource(promptEn, promptZh).Length > 0);
            // When the language model has to write or translate the prompt, the submission is answered at once and the prompt is written in the background (F56).
            bool background = compose && PromptRules.NeedsModel(description, promptEn, promptZh);
            if (background && PreparingCount(session.User.UserID) >= MaxPreparingPerUser)
            {
                return Error("还有几个任务正在生成提示词，请等它们开始执行后再提交。", "queue_limit");
            }
            if (compose && !background)
            {
                composed = WithBrand(await PromptService.ComposePanelAsync(session, styleId, productTypeId, needModel, description, promptEn, promptZh), brandFragment);
                templateInput = (JObject)rawInput.DeepClone();
                templateInput["prompt"] = composed.Prompt;
            }
            else if (!compose && brandFragment.Length > 0)
            {
                templateInput = (JObject)rawInput.DeepClone();
                templateInput["prompt"] = JoinPrompt(rawInput["prompt"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt") : "", brandFragment);
            }
            // The template decides the workflow and presets; the client's own workflow_id and workflow_params are replaced.
            // A background submission is resolved now with an empty subject, so invalid requests still fail at once; the prompt is filled in later.
            JObject resolved = TemplateApi.Resolve(session, templateInput, requestedTemplate, out JObject templateError, out string preserveToken, generationSize, options);
            if (resolved is null)
            {
                return templateError;
            }
            // Local postprocessing (R10.3, in GenerationFinish since R09): scale to the chosen size first, then paste the original product
            // pixels back at that size, so scaling never blurs the product's logo or text; both happen before the works are synced or shown.
            Func<JObject, Task> postProcess = preserveToken is null && outputSize is null ? null
                : result => PostProcess.GenerationFinish.ApplyAsync(session.User, preserveToken, outputSize, result);
            if (background)
            {
                return StartPreparing(session, resolved, requestedTemplate, pinnedServer, preserveToken, postProcess,
                    async () => WithBrand(await PromptService.ComposePanelAsync(session, styleId, productTypeId, needModel, description, promptEn, promptZh), brandFragment), options.RawPrompt);
            }
            JObject templateResult = await GeneratePublishedWorkflow(session, resolved, resolved.Value<string>("workflow_id"), pinnedServer, requestedTemplate, postProcess, options);
            if (templateResult.ContainsKey("error"))
            {
                PostProcess.GenerationFinish.Discard(preserveToken);
            }
            else
            {
                AddPromptFields(templateResult, composed);
            }
            return templateResult;
        }
        string requestedWorkflow = (string)rawInput["workflow_id"];
        if (!string.IsNullOrWhiteSpace(requestedWorkflow))
        {
            return await GeneratePublishedWorkflow(session, rawInput, requestedWorkflow, pinnedServer, options: options);
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
            ["extra_metadata"] = new JObject() { [ExecutionScopeKey] = ExecutionScopeValue }
        };
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, uint.MaxValue, true)
            || !CopyOptionalNumber(rawInput, input, "steps", 1, 100, true)
            || !CopyOptionalNumber(rawInput, input, "cfgscale", 0, 30, false))
        {
            return Error("种子、步数或提示词强度不在允许范围内。", "invalid_parameters");
        }

        // ----- Reference / edit images -----
        JObject referenceError = AttachReferenceImage(rawInput, input, mode, kind);
        if (referenceError is not null)
        {
            return referenceError;
        }

        // ----- Mask (inpaint / outpaint) -----
        JObject maskError = AttachMask(rawInput, input, mode);
        if (maskError is not null)
        {
            return maskError;
        }

        // ----- Upscale -----
        JObject upscaleError = AttachUpscale(rawInput, input, mode);
        if (upscaleError is not null)
        {
            return upscaleError;
        }

        // ----- LoRAs -----
        JObject loraError = AttachLoras(session, rawInput, input);
        if (loraError is not null)
        {
            return loraError;
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
        return await ScheduleAndSubmit(session, images, input, pinnedServer, false, rawInput.Value<bool?>("async") is true, StandardModeProblems);
    }

    /// <summary>Validates and attaches the initial or edit image of a standard-mode request (img2img / inpaint / outpaint / upscale);
    /// the check order and error texts are part of the API contract. Returns the error, or null when accepted.</summary>
    private static JObject AttachReferenceImage(JObject rawInput, JObject input, string mode, string kind)
    {
        string reference = (string)rawInput["initimage"];
        bool needsReference = mode is "img2img" or "inpaint" or "outpaint" or "upscale";
        if (needsReference && string.IsNullOrEmpty(reference))
        {
            return Error("该创作方式需要先提供初始图。", "invalid_reference");
        }
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }
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
        return null;
    }

    /// <summary>Validates and attaches the mask of an inpaint / outpaint request. Returns the error, or null when accepted.</summary>
    private static JObject AttachMask(JObject rawInput, JObject input, string mode)
    {
        string mask = (string)rawInput["maskimage"];
        if (mode is not ("inpaint" or "outpaint"))
        {
            return null;
        }
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
        return null;
    }

    /// <summary>Validates and attaches the upscale factor of an upscale request. Returns the error, or null when accepted.</summary>
    private static JObject AttachUpscale(JObject rawInput, JObject input, string mode)
    {
        if (mode != "upscale")
        {
            return null;
        }
        if (!CopyOptionalNumber(rawInput, input, "refinerupscale", 1.5, 4, false) || input["refinerupscale"] is null)
        {
            return Error("请提供 1.5–4 的放大倍数。", "invalid_parameters");
        }
        // Keep the base stage close to the supplied image, then refine at the larger size.
        input["initimagecreativity"] = 0.25;
        return null;
    }

    /// <summary>Validates and attaches the LoRA selection of a standard-mode request against the account's allowed list.
    /// Returns the error, or null when accepted.</summary>
    private static JObject AttachLoras(Session session, JObject rawInput, JObject input)
    {
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
        return null;
    }

    /// <summary>Joins prompt parts with commas, skipping empty ones.</summary>
    private static string JoinPrompt(params string[] parts) => string.Join(", ", parts.Select(part => part?.Trim()).Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>The composed prompt with a brand kit's fragment (F12) added; unchanged without one.</summary>
    private static PromptService.Composed WithBrand(PromptService.Composed composed, string brandFragment) =>
        string.IsNullOrEmpty(brandFragment) ? composed : composed with { Prompt = JoinPrompt(composed.Prompt, brandFragment) };

    /// <summary>Adds the prompt this account's own submission was sent with (F54) to its response, so the result card can show it, plus any fallback notice.
    /// Only the submitter's own answers carry it; the shared queue never does.</summary>
    private static void AddPromptFields(JObject result, PromptService.Composed composed)
    {
        if (composed is null)
        {
            return;
        }
        result["prompt_used"] = composed.Prompt;
        if (composed.Translated is not null)
        {
            result["prompt_translated"] = composed.Translated;
        }
        if (composed.NoticeCode is not null)
        {
            result["prompt_notice"] = composed.Notice;
            result["prompt_notice_code"] = composed.NoticeCode;
        }
    }
}
