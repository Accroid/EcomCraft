using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>StartSet and its per-step helpers: reading products, references and storyboard, choosing templates and submitting the batch.
/// Part of the BatchService partial class (R10); this is the seam where T85 and T98 will extend the flow.</summary>
public static partial class BatchService
{
    /// <summary>The step-2 options that feed the shared batch prompt (F54/F56), read by <see cref="StartSet"/> before the background composition starts.</summary>
    private sealed record PromptInputs(string Description, string StyleId, string ProductTypeId, bool NeedModel, string PromptEn, string PromptZh);

    /// <summary>Validates a one-click set, registers it and starts composing its prompt in the background (F56).</summary>
    private static JObject StartSet(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration) || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        Cleanup();
        if (rawInput["products"] is not JArray products || products.Count == 0 || products.Count > SetRules.MaxProducts)
        {
            return StudioApi.Error($"请提供 1–{SetRules.MaxProducts} 张商品图。", "invalid_reference");
        }
        Batch batch = new() { Id = Guid.NewGuid().ToString("N"), UserId = session.User.UserID, Created = Environment.TickCount64 };
        JObject productsError = ReadProducts(session, products, batch);
        if (productsError is not null)
        {
            return productsError;
        }
        string language = rawInput.Value<string>("language") == "en" ? "en" : "zh-CN";
        string platform = rawInput["platform"]?.Type == JTokenType.String ? rawInput.Value<string>("platform") : "";
        string setId = rawInput["set_id"]?.Type == JTokenType.String ? rawInput.Value<string>("set_id") : "standard";
        int candidates = rawInput["candidates"]?.Type == JTokenType.Integer ? rawInput.Value<int>("candidates") : 4;
        if (candidates < 1 || candidates > SetRules.MaxCandidates)
        {
            return StudioApi.Error($"每种图片的候选张数须为 1–{SetRules.MaxCandidates}。", "invalid_parameters");
        }
        batch.Candidates = candidates;
        batch.Platform = platform;
        if (rawInput["execution_server_id"]?.Type == JTokenType.Integer)
        {
            int pinned = rawInput.Value<int>("execution_server_id");
            if (!ExecutionServers.All.Any(data => data.ID == pinned))
            {
                return StudioApi.Error("所选执行服务器不存在或已被停用，请刷新后重新选择。", "invalid_execution_server");
            }
            batch.Server = pinned;
        }
        List<(EcomTemplate Template, string WorkflowId, string ImageField)> runnable = TemplateApi.RunnableTemplates(session);
        List<SetRules.TemplateRef> references = [.. runnable.Select(item => new SetRules.TemplateRef(item.Template.Id, item.Template.Platforms, item.Template.Sets, item.Template.Kind))];
        List<SetRules.Job> expanded;
        // Reference mode (F59): one image per reference, prompts already final and user-edited, a fixed scene template, and no other step-2 options.
        if (rawInput["reference_prompts"] is JArray promptItems)
        {
            JObject referenceError = ReadReferences(rawInput, batch, promptItems);
            if (referenceError is not null)
            {
                return referenceError;
            }
            SetRules.TemplateRef scene = references.FirstOrDefault(template => template.Kind == "scene_main" && template.Sets.Contains(setId) && SetRules.SuitsPlatform(template, platform));
            if (scene is null)
            {
                // Say why: the scene template runs on the system-generated scene workflow (F57) unless an administrator published another one.
                string reason = StandardWorkflows.Unavailable("scene");
                return StudioApi.Error(reason is null
                    ? "参考模式需要“场景图”模板，但它现在不可用：执行服务器可能离线，或模板已被停用。请确认执行服务器在线，或请管理员检查模板。"
                    : $"参考模式需要“场景图”模板，但它用的场景图工作流还不可用：{reason}", "invalid_template");
            }
            batch.Candidates = 1;
            candidates = 1;
            expanded = SetRules.ExpandReferences(batch.Products.Count, scene.Id, batch.ReferencePrompts.Count);
        }
        else if (rawInput["shots"] is JArray shotItems && shotItems.Count > 0)
        {
            JObject shotsError = ReadStoryboard(rawInput, batch, shotItems, references, platform, out List<string> shotTemplates);
            if (shotsError is not null)
            {
                return shotsError;
            }
            expanded = SetRules.ExpandShots(batch.Products.Count, shotTemplates);
        }
        else
        {
            (List<SetRules.TemplateRef> setTemplates, string slotError) = SlotTemplates(rawInput, references, platform, setId);
            if (slotError is not null)
            {
                return StudioApi.Error(slotError, "invalid_parameters");
            }
            if (setTemplates.Count == 0)
            {
                return StudioApi.Error("这个平台暂时没有可用的套图模板。请管理员发布模板，或确认执行服务器在线。", "invalid_template");
            }
            expanded = SetRules.Expand(batch.Products.Count, setTemplates);
        }
        if (expanded.Count > SetRules.MaxJobs)
        {
            return StudioApi.Error($"一次最多 {SetRules.MaxJobs} 个任务，请减少商品数量。", "invalid_parameters");
        }
        // One shared cap for single generations and whole sets alike (F55): products × set types × candidates may not exceed the image total.
        if (!SetRules.WithinImageTotal(expanded.Count, candidates))
        {
            return StudioApi.Error($"预计 {expanded.Count * candidates} 张，超过 {SetRules.MaxImages} 张总上限，请减少商品图、类型或候选张数。", "invalid_parameters");
        }
        foreach (SetRules.Job job in expanded)
        {
            EcomTemplate template = runnable.First(item => item.Template.Id == job.Template).Template;
            batch.Jobs.Add(new Job()
            {
                Id = batch.Jobs.Count, Product = job.Product, Template = job.Template, TemplateName = EcomTemplate.Pick(template.Name, language),
                Shot = job.Shot, Reference = job.Reference,
                Purpose = job.Shot >= 0 && batch.ShotPurposes[job.Shot].Length > 0 ? batch.ShotPurposes[job.Shot] : null
            });
        }
        // In reference mode none of the other step-2 options apply (F59): no description, style, product type, prompt panel or brand kit.
        PromptInputs inputs = null;
        if (batch.ReferencePrompts is null)
        {
            JObject optionsError = ReadPromptOptions(session, rawInput, batch, out inputs);
            if (optionsError is not null)
            {
                return optionsError;
            }
        }
        batch.Negative = rawInput["negativeprompt"]?.Type == JTokenType.String ? rawInput.Value<string>("negativeprompt") : "";
        if (rawInput["output_size"] is JObject)
        {
            if (!OutputSizeRules.TryParse(rawInput["output_size"], out int outputWidth, out int outputHeight))
            {
                return StudioApi.Error($"出图尺寸须为 {OutputSizeRules.MinSide}–{OutputSizeRules.MaxSide} 像素的整数。", "invalid_parameters");
            }
            batch.OutputSize = new JObject() { ["width"] = outputWidth, ["height"] = outputHeight };
        }
        return SubmitBatch(session, batch, inputs);
    }

    /// <summary>Reads the batch's product photos: a saved product by ID (with its cut-out and placement, F36) or an inline data URL, each with an optional placement.
    /// Returns the error, or null when accepted.</summary>
    private static JObject ReadProducts(Session session, JArray products, Batch batch)
    {
        foreach (JToken item in products)
        {
            Product product = new();
            if (item?["product_id"]?.Type == JTokenType.String)
            {
                // A saved product: its cut-out (when confirmed) is used for every template; otherwise the original photo.
                (string saved, Placement placementSaved, string problem) = Products.ProductLibrary.ForGeneration(session.User.UserID, item.Value<string>("product_id"), false);
                if (saved is null)
                {
                    return StudioApi.Error(problem, "invalid_reference");
                }
                (string cutout, _, _) = Products.ProductLibrary.ForGeneration(session.User.UserID, item.Value<string>("product_id"), true);
                product.Image = saved;
                product.Cutout = cutout;
                product.Placement = placementSaved;
            }
            else
            {
                string image = item?["image"]?.Type == JTokenType.String ? item.Value<string>("image") : null;
                if (!SetRules.ProductImageLooksValid(image))
                {
                    return StudioApi.Error("商品图须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference");
                }
                product.Image = image;
            }
            if (item["placement"] is JObject placement)
            {
                if (!Placement.TryCreate(placement.Value<double?>("x"), placement.Value<double?>("y"), placement.Value<double?>("size"), out Placement parsed))
                {
                    return StudioApi.Error("商品位置无效：中心须在画面内，大小在 0.1–1.5 之间。", "invalid_parameters");
                }
                product.Placement = parsed;
            }
            batch.Products.Add(product);
        }
        return null;
    }

    /// <summary>Reads reference mode's final user-edited prompts, the optional per-reference placements (T79) and the optional reference images (F59) into the
    /// batch; the images stay in memory only for the batch's lifetime. Returns the error, or null when accepted.</summary>
    private static JObject ReadReferences(JObject rawInput, Batch batch, JArray promptItems)
    {
        if (promptItems.Count == 0 || promptItems.Count > SetRules.MaxReferences)
        {
            return StudioApi.Error($"参考图须为 1–{SetRules.MaxReferences} 张。", "invalid_parameters");
        }
        if (rawInput["shots"] is JArray withShots && withShots.Count > 0)
        {
            return StudioApi.Error("分镜与参考图不能同时使用，请二选一。", "invalid_parameters");
        }
        batch.ReferencePrompts = [];
        foreach (JToken item in promptItems)
        {
            string rawPrompt = item?.Type == JTokenType.String ? item.ToString().Trim() : "";
            if (rawPrompt.Length == 0 || rawPrompt.Length > PromptRules.MaxPromptLength)
            {
                return StudioApi.Error($"每张参考图都需要 1–{PromptRules.MaxPromptLength} 个字符的英文提示词。", "invalid_prompt");
            }
            string cleaned = PromptRules.CleanUserPrompt(rawPrompt);
            if (cleaned.Length == 0)
            {
                return StudioApi.Error("参考提示词无效，请修改后重试。", "invalid_prompt");
            }
            batch.ReferencePrompts.Add(cleaned);
        }
        // Optional placement per reference (T79): null keeps the product's own placement.
        if (rawInput["reference_placements"] is JArray placementItems)
        {
            if (placementItems.Count != promptItems.Count)
            {
                return StudioApi.Error("参考图摆放的数量与提示词数量不一致。", "invalid_parameters");
            }
            foreach (JToken item in placementItems)
            {
                if (item is null || item.Type == JTokenType.Null)
                {
                    batch.ReferencePlacements.Add(null);
                    continue;
                }
                if (item is not JObject spot || !Placement.TryCreate(spot.Value<double?>("x"), spot.Value<double?>("y"), spot.Value<double?>("size"), out Placement parsed))
                {
                    return StudioApi.Error("商品位置无效：中心须在画面内，大小在 0.1–1.5 之间。", "invalid_parameters");
                }
                batch.ReferencePlacements.Add(parsed);
            }
        }
        // Optional images, kept in memory only for the batch's lifetime (F59); P1 sends prompts alone.
        if (rawInput["reference_images"] is JArray imageItems)
        {
            if (imageItems.Count != promptItems.Count)
            {
                return StudioApi.Error("参考图数量与提示词数量不一致。", "invalid_parameters");
            }
            foreach (JToken item in imageItems)
            {
                string image = item?.Type == JTokenType.String ? item.ToString() : "";
                if (!ImageDataUrl.IsValid(image, ImageDataUrl.ImageDataFormats.AnyImage, SetRules.MaxImageDataLength))
                {
                    return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
                }
                batch.ReferenceImages.Add(image);
            }
        }
        return null;
    }

    /// <summary>Reads a confirmed storyboard (F25): every shot is checked again, since the page may have changed it; only template IDs and cleaned text are kept.
    /// Returns the error, or null when accepted, with each shot's template ID in <paramref name="shotTemplates"/>.</summary>
    private static JObject ReadStoryboard(JObject rawInput, Batch batch, JArray shotItems, List<SetRules.TemplateRef> references, string platform,
        out List<string> shotTemplates)
    {
        if (shotItems.Count > StoryboardRules.MaxShots)
        {
            shotTemplates = null;
            return StudioApi.Error($"分镜最多 {StoryboardRules.MaxShots} 张。", "invalid_parameters");
        }
        shotTemplates = [];
        foreach (JToken item in shotItems)
        {
            if (item is not JObject shot)
            {
                return StudioApi.Error("分镜格式无效，请重新生成分镜。", "invalid_parameters");
            }
            string templateId = shot["template_id"]?.Type == JTokenType.String ? shot.Value<string>("template_id") : "";
            SetRules.TemplateRef reference = references.FirstOrDefault(template => template.Id == templateId);
            if (reference is null || !SetRules.SuitsPlatform(reference, platform))
            {
                return StudioApi.Error("分镜里有当前平台不可用的图片类型，请重新选择后再出图。", "invalid_template");
            }
            string fragment = shot["prompt"]?.Type == JTokenType.String ? shot.Value<string>("prompt") : "";
            string purpose = shot["purpose"]?.Type == JTokenType.String ? shot.Value<string>("purpose") : "";
            if (fragment.Length > PromptRules.MaxPromptLength || purpose.Length > 500)
            {
                return StudioApi.Error($"分镜的提示词最多 {StoryboardRules.MaxFragmentLength} 个字符。", "invalid_prompt");
            }
            shotTemplates.Add(templateId);
            batch.ShotPrompts.Add(StoryboardRules.CleanFragment(fragment));
            batch.ShotPurposes.Add(StoryboardRules.CleanText(purpose, StoryboardRules.MaxPurposeLength));
        }
        return null;
    }

    /// <summary>Reads the step-2 options that feed the shared batch prompt (F54/F56) and checks the brand kit now, so a missing kit fails at once (F12);
    /// every task then sends the kit's ID through the normal template path. Returns the error, or null when accepted.</summary>
    private static JObject ReadPromptOptions(Session session, JObject rawInput, Batch batch, out PromptInputs inputs)
    {
        string description = rawInput["description"]?.Type == JTokenType.String ? rawInput.Value<string>("description").Trim() : "";
        if (description.Length > PromptService.MaxDescriptionLength)
        {
            inputs = null;
            return StudioApi.Error($"描述最多 {PromptService.MaxDescriptionLength} 个字符。", "invalid_prompt");
        }
        string style = rawInput["style_id"]?.Type == JTokenType.String ? rawInput.Value<string>("style_id") : "";
        string productTypeId = rawInput["product_type_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_type_id") : "";
        bool needModel = rawInput["need_model"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("need_model");
        if (!PromptService.KnownProductType(productTypeId))
        {
            inputs = null;
            return StudioApi.Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
        }
        string promptEn = rawInput["prompt_en"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_en") : "";
        string promptZh = rawInput["prompt_zh"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_zh") : "";
        if (promptEn.Length > PromptRules.MaxPromptLength || promptZh.Length > PromptRules.MaxPromptLength)
        {
            inputs = null;
            return StudioApi.Error($"提示词最多 {PromptRules.MaxPromptLength} 个字符。", "invalid_prompt");
        }
        if (rawInput["brand_kit_id"]?.Type == JTokenType.String && rawInput.Value<string>("brand_kit_id").Length > 0)
        {
            (_, string brandProblem) = Brand.BrandKitLibrary.FragmentFor(session.User.UserID, rawInput.Value<string>("brand_kit_id"));
            if (brandProblem is not null)
            {
                inputs = null;
                return StudioApi.Error(brandProblem, "invalid_brand_kit");
            }
            batch.BrandKitId = rawInput.Value<string>("brand_kit_id");
        }
        inputs = new PromptInputs(description, style, productTypeId, needModel, promptEn, promptZh);
        return null;
    }

    /// <summary>Registers the batch and starts its tasks: reference mode at once (F59, its prompts are already final), otherwise once the shared prompt is
    /// written in the background (F56).</summary>
    private static JObject SubmitBatch(Session session, Batch batch, PromptInputs inputs)
    {
        foreach (Job job in batch.Jobs)
        {
            job.State = batch.ReferencePrompts is null ? "prompt" : "queued";
        }
        Batches[batch.Id] = batch;
        if (batch.ReferencePrompts is not null)
        {
            // Reference prompts are already final and user-edited (F59): no background composition, the tasks start at once.
            batch.Composed = new PromptService.Composed("", "reference", null, null);
            foreach (Job job in batch.Jobs)
            {
                _ = Utilities.RunCheckedTask(() => RunJobAsync(session, batch, job), "ecomcraft batch task");
            }
            return Status(batch);
        }
        // The batch is answered at once; the prompt is composed once in the background (F56) and the tasks start when it is ready.
        // The bilingual prompt panel (F54) decides the prompt when it holds text; otherwise the one-line description is composed as before.
        _ = Utilities.RunCheckedTask(async () =>
        {
            PromptService.Composed composed = await PromptService.ComposePanelAsync(session, inputs.StyleId, inputs.ProductTypeId, inputs.NeedModel,
                inputs.Description, inputs.PromptEn, inputs.PromptZh);
            // Storyboard fragments written in Chinese are translated too (F25); one that cannot be translated is left out rather than sent untranslated.
            string[] shotPrompts = await Task.WhenAll(batch.ShotPrompts.Select(async fragment => fragment.Length == 0 || PromptRules.IsAscii(fragment)
                ? fragment : StoryboardRules.CleanFragment(await PromptService.TranslateToEnglishAsync(session, fragment))));
            TaskTiming.RecordSince(TaskTiming.Compose, batch.Created);
            List<Job> ready;
            lock (batch.Lock)
            {
                batch.ShotsUntranslated = shotPrompts.Where((fragment, index) => fragment.Length == 0 && batch.ShotPrompts[index].Length > 0).Any();
                batch.ShotPrompts = [.. shotPrompts];
                batch.Prompt = composed.Prompt;
                batch.Composed = composed;
                ready = [.. batch.Jobs.Where(job => job.State == "prompt")];
                foreach (Job job in ready)
                {
                    job.State = "queued";
                }
            }
            foreach (Job job in ready)
            {
                _ = Utilities.RunCheckedTask(() => RunJobAsync(session, batch, job), "ecomcraft batch task");
            }
        }, "ecomcraft batch prompt");
        return Status(batch);
    }

    /// <summary>Templates of a set for one platform (F58): when the platform defines slots, composed by slot kind — every non-optional slot plus the
    /// optional slots the request ticked, kinds deduplicated because same-kind optional slots only add export targets, not extra generations. Platforms
    /// without slots keep the old composition by set membership. The error is set when a ticked slot does not belong to the platform.</summary>
    private static (List<SetRules.TemplateRef> Templates, string Error) SlotTemplates(JObject rawInput, List<SetRules.TemplateRef> references, string platform, string setId)
    {
        JObject platformData = string.IsNullOrEmpty(platform) ? null : TemplateStore.Platforms().FirstOrDefault(item => item["id"]?.ToString() == platform);
        List<ExportSlot> slots = platformData is null ? null : ExportRules.ParseSlots(platformData, out _);
        if (slots is null)
        {
            return (SetRules.SetTemplates(references, platform, setId), null);
        }
        HashSet<string> ticked = [];
        foreach (JToken token in rawInput["optional_slots"] as JArray ?? new JArray())
        {
            string id = token?.Type == JTokenType.String ? token.ToString() : "";
            if (!slots.Any(slot => slot.Id == id && slot.Optional))
            {
                return (null, "所选的可选槽位不存在，请刷新后重新选择。");
            }
            ticked.Add(id);
        }
        List<string> kinds = [.. slots.Where(slot => !slot.Optional || ticked.Contains(slot.Id)).Select(slot => slot.Kind).Distinct()];
        return (SetRules.SlotTemplates(references, platform, setId, kinds), null);
    }
}
