using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Setup;
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
    private static async Task<JObject> StartSet(Session session, JObject rawInput)
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
        batch.Language = language;
        batch.Platform = platform;
        int candidates = rawInput["candidates"]?.Type == JTokenType.Integer ? rawInput.Value<int>("candidates") : 4;
        if (candidates < 1 || candidates > SetRules.MaxCandidates)
        {
            return StudioApi.Error($"每种图片的候选张数须为 1–{SetRules.MaxCandidates}。", "invalid_parameters");
        }
        batch.Candidates = candidates;
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
        // The direct-fusion trial (F84) is a second reference mode: the images are required but the prompts are written by the server instead.
        string referenceMode = rawInput["reference_mode"]?.Type == JTokenType.String ? rawInput.Value<string>("reference_mode") : "";
        if (!SetRules.IsValidReferenceMode(referenceMode))
        {
            return StudioApi.Error("未知的参考图模式。", "invalid_parameters");
        }
        if (referenceMode == SetRules.ReferenceModeEdit)
        {
            JObject editError = await StartEditSet(session, rawInput, batch, runnable);
            if (editError is not null)
            {
                return editError;
            }
            JObject slotError = ExpandSlotBatch(session, rawInput, batch, platform, language, "reference-edit", out expanded);
            if (slotError is not null)
            {
                return slotError;
            }
            if (expanded is null)
            {
                expanded = SetRules.ExpandReferences(batch.Products.Count, "reference-edit", batch.ReferenceImages.Count);
            }
        }
        else if (rawInput["reference_prompts"] is JArray promptItems)
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
            JObject slotError = ExpandSlotBatch(session, rawInput, batch, platform, language, scene.Id, out expanded);
            if (slotError is not null)
            {
                return slotError;
            }
            if (expanded is null)
            {
                batch.Candidates = 1;
                candidates = 1;
                expanded = SetRules.ExpandReferences(batch.Products.Count, scene.Id, batch.ReferencePrompts.Count);
            }
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
        // One shared cap for single generations and whole sets alike (F55): products × set types × candidates may not exceed the image total. The cap is
        // configurable per server since T98 (default 60); a larger batch is refused outright, never silently truncated.
        int imageLimit = SetRules.ImageLimit(Program.ServerSettings.EcomCraft.BatchImageLimit);
        int totalImages = batch.ReferenceSlots is not null ? SetRules.SlotImageTotal(expanded, batch.Candidates) : expanded.Count * batch.Candidates;
        if (totalImages > imageLimit)
        {
            return StudioApi.Error($"预计 {totalImages} 张，超过 {imageLimit} 张总上限，请减少商品图、图位或候选张数。", "invalid_parameters");
        }
        bool customSize = rawInput["output_size"] is JObject;
        Dictionary<string, string> slotNames = SlotNames(platform, language);
        foreach (SetRules.Job job in expanded)
        {
            // Slot tasks the server makes itself (T98): no template behind them, so their card carries the slot's name and the slot's own size.
            if (job.Source is (SetRules.SourceCutout or SetRules.SourceCrop or SetRules.SourceLayout))
            {
                batch.Jobs.Add(new Job()
                {
                    Id = batch.Jobs.Count, Product = job.Product, Template = "", TemplateName = slotNames.GetValueOrDefault(job.SlotId, job.SlotKind),
                    Reference = job.Reference, SlotId = job.SlotId, SlotKind = job.SlotKind, Source = job.Source,
                    OutputSize = customSize ? null : SlotSizeOf(platform, job.SlotId)
                });
                continue;
            }
            EcomTemplate template = runnable.First(item => item.Template.Id == job.Template).Template;
            batch.Jobs.Add(new Job()
            {
                Id = batch.Jobs.Count, Product = job.Product, Template = job.Template, TemplateName = EcomTemplate.Pick(template.Name, language),
                Shot = job.Shot, Reference = job.Reference, SlotId = job.SlotId, SlotKind = job.SlotKind, Source = job.Source,
                // A reference left without a slot in a slot batch keeps its old single picture (T98).
                ImageCount = batch.ReferenceSlots is not null && job.SlotId is null ? 1 : null,
                Purpose = job.Shot >= 0 && batch.ShotPurposes[job.Shot].Length > 0 ? batch.ShotPurposes[job.Shot] : null,
                OutputSize = job.SlotId is null || customSize ? null : SlotSizeOf(platform, job.SlotId)
            });
        }
        // The selling slot draws on the product's first finished fusion picture (T98): one waiter per product, completed by whichever fusion task
        // finishes with pictures first.
        if (batch.Jobs.Any(job => job.Source == SetRules.SourceLayout))
        {
            foreach (int product in batch.Jobs.Where(job => job.Source == SetRules.SourceLayout).Select(job => job.Product).Distinct())
            {
                batch.FusionBase[product] = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                // A product with no fusion task never gets a fusion picture: its layouts start at once (detail screens then use the product's photo).
                if (!batch.Jobs.Any(job => job.Product == product && job.Source == SetRules.SourceFusion))
                {
                    batch.FusionBase[product].TrySetResult(null);
                }
            }
        }
        // In reference mode none of the other step-2 options apply (F59): no description, style, product type, prompt panel or brand kit.
        // The edit trial composes its own prompt server-side per product (F84), so it starts at once too.
        PromptInputs inputs = null;
        if (batch.ReferencePrompts is null && !batch.ReferenceEdit)
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
                string productId = item.Value<string>("product_id");
                (string saved, Placement placementSaved, string problem) = Products.ProductLibrary.ForGeneration(session.User.UserID, productId, false);
                if (saved is null)
                {
                    return StudioApi.Error(problem, "invalid_reference");
                }
                (string cutout, _, _) = Products.ProductLibrary.ForGeneration(session.User.UserID, productId, true);
                product.Image = saved;
                product.Cutout = cutout;
                product.Placement = placementSaved;
                product.ProductId = productId;
            }
            else
            {
                string image = item?["image"]?.Type == JTokenType.String ? item.Value<string>("image") : null;
                if (!SetRules.ProductImageLooksValid(image))
                {
                    return StudioApi.Error("商品图须为不超过 5 MB 的 PNG、JPEG 或 WebP 图片。", "invalid_reference");
                }
                product.Image = image;
                // The optional pre-cut-out original (T98): the photo the detail slots crop from. Anything unreadable is simply not kept — the crop then
                // falls back to the photo the request did carry.
                string original = item?["original"]?.Type == JTokenType.String ? item.Value<string>("original") : null;
                if (original is not null && ImageDataUrl.IsValid(original, ImageDataUrl.ImageDataFormats.PngJpegWebp, SetRules.MaxImageDataLength))
                {
                    product.Original = original;
                }
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
                if (!ImageDataUrl.HasImagePrefix(image, SetRules.MaxImageDataLength))
                {
                    return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
                }
                batch.ReferenceImages.Add(image);
            }
        }
        return null;
    }

    /// <summary>Validates the direct-fusion trial's batch (F84, T123): reference images are required but per-reference prompts are not sent — the prompt
    /// is written by the server per product. Checks the reference-edit template and the workflow of every product's angle count, and reads the angle IDs of
    /// saved products. Returns the error, or null when accepted.</summary>
    private static async Task<JObject> StartEditSet(Session session, JObject rawInput, Batch batch, List<(EcomTemplate Template, string WorkflowId, string ImageField)> runnable)
    {
        if (rawInput["reference_prompts"] is JArray)
        {
            return StudioApi.Error("直接融合不需要逐张提示词，提示词由服务端生成。", "invalid_prompt");
        }
        if (rawInput["shots"] is JArray withShots && withShots.Count > 0)
        {
            return StudioApi.Error("分镜与参考图不能同时使用，请二选一。", "invalid_parameters");
        }
        JArray imageItems = rawInput["reference_images"] as JArray;
        if (imageItems is null || imageItems.Count == 0 || imageItems.Count > SetRules.MaxReferences)
        {
            return StudioApi.Error($"参考图须为 1–{SetRules.MaxReferences} 张。", "invalid_parameters");
        }
        foreach (JToken item in imageItems)
        {
            string image = item?.Type == JTokenType.String ? item.ToString() : "";
            if (!ImageDataUrl.HasImagePrefix(image, SetRules.MaxImageDataLength))
            {
                return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
            }
            batch.ReferenceImages.Add(image);
        }
        // The trial template must be runnable at all, and every product needs the workflow of its own angle count (1–3).
        if (runnable.All(item => item.Template.Id != "reference-edit"))
        {
            return StudioApi.Error("直接融合试用版现在不可用，请稍后再试或联系管理员。", "invalid_template");
        }
        Dictionary<string, JObject> workflows = TemplateApi.Runnable(session);
        foreach (Product product in batch.Products)
        {
            if (product.ProductId is null)
            {
                // An inline photo: a single angle.
                continue;
            }
            (List<string> ids, string problem) = Products.ProductLibrary.AngleIdsFor(session.User.UserID, product.ProductId);
            if (ids is null)
            {
                return StudioApi.Error(problem, "invalid_reference");
            }
            product.AngleIds = ids;
        }
        foreach (int angles in batch.Products.Select(product => Math.Clamp(product.AngleIds?.Count ?? 1, 1, 3)).Distinct())
        {
            if (!workflows.ContainsKey(StandardWorkflows.ReferenceEditId(angles)))
            {
                string reason = StandardWorkflows.Unavailable(StandardWorkflows.ReferenceEditKind(angles));
                return StudioApi.Error(reason is null
                    ? "直接融合的工作流现在不可用：执行服务器可能离线。请确认执行服务器在线，或请管理员检查。"
                    : $"直接融合的工作流还不可用：{reason}", "invalid_template");
            }
        }
        batch.ReferenceEdit = true;
        batch.Candidates = 1;
        batch.EditResolution = HardwareRules.ReferenceEditResolution(Program.ServerSettings.EcomCraft.ReferenceEditResolution, await HardwareProbe.CurrentTierAsync(),
            ModelRecipes.Current().Recipe?.ReferenceEdit);
        return null;
    }

    /// <summary>The slot modes of the reference flow (T98): references with a user-chosen slot each, or one reference expanded into the platform's whole
    /// set. A request without either keeps the slot-less shape exactly as before. Slot batches take the request's candidate count (defaulting to the
    /// server's configured slot candidates); uncovered slots are recorded for the page, never invented (A20). Returns the error, or null with the
    /// expansion — null in <paramref name="expanded"/> means the request is slot-less and the caller expands as before.</summary>
    private static JObject ExpandSlotBatch(Session session, JObject rawInput, Batch batch, string platform, string language, string fusionTemplateId,
        out List<SetRules.Job> expanded)
    {
        expanded = null;
        int references = batch.ReferenceEdit ? batch.ReferenceImages.Count : batch.ReferencePrompts.Count;
        bool fullSet = rawInput["full_set"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("full_set");
        JArray slotItems = rawInput["reference_slots"] as JArray;
        if (slotItems is null && !fullSet)
        {
            return null;
        }
        JObject platformData = string.IsNullOrEmpty(platform) ? null : TemplateStore.Platforms().FirstOrDefault(item => item["id"]?.ToString() == platform);
        List<ExportSlot> slots = platformData is null ? null : ExportRules.ParseSlots(platformData, out _);
        if (slots is null)
        {
            return StudioApi.Error("这个平台还没有定义图位，不能按图位出图，请换一个平台或直接按参考出图。", "invalid_slot");
        }
        // Every reference carries one slot of its own (T98): a null entry keeps that reference's slot-less behaviour.
        List<(string SlotId, string Kind)> chosen = [];
        if (slotItems is not null)
        {
            if (slotItems.Count != references)
            {
                return StudioApi.Error("图位选择的数量与参考图数量不一致。", "invalid_parameters");
            }
            foreach (JToken token in slotItems)
            {
                string id = token?.Type == JTokenType.String ? token.ToString() : "";
                if (token is null || token.Type == JTokenType.Null || id.Length == 0)
                {
                    chosen.Add((null, null));
                    continue;
                }
                ExportSlot slot = slots.FirstOrDefault(item => item.Id == id);
                if (slot is null || !SetRules.SetSlotKinds.Contains(slot.Kind))
                {
                    return StudioApi.Error("所选图位不属于这个平台，请刷新后重新选择。", "invalid_slot");
                }
                chosen.Add((slot.Id, slot.Kind));
            }
        }
        if (!fullSet)
        {
            if (!chosen.Any(slot => slot.SlotId is not null))
            {
                // Only null entries: the slot-less shape as before (T98).
                return null;
            }
            batch.ReferenceSlots = chosen;
            batch.Candidates = SlotCandidatesOf(rawInput);
            expanded = SetRules.ExpandReferenceSlots(batch.Products.Count, fusionTemplateId, chosen);
            // Slots of the platform no reference covers show as uncovered (T98): per product, in slot order, kinds that join sets only.
            foreach (int product in Enumerable.Range(0, batch.Products.Count))
            {
                foreach (ExportSlot slot in ComposedSlots(rawInput, slots))
                {
                    if (!chosen.Any(entry => entry.SlotId == slot.Id))
                    {
                        batch.Uncovered.Add((product, slot.Id, slot.Kind, "no_reference"));
                    }
                }
            }
            return null;
        }
        if (references != 1)
        {
            return StudioApi.Error("出整套只支持一张参考图；多张参考图时请为每张选择图位。", "invalid_parameters");
        }
        if (slotItems is not null && chosen.Any(slot => slot.SlotId is not null))
        {
            return StudioApi.Error("出整套时不选图位，整套图由这一张参考图展开。", "invalid_parameters");
        }
        batch.ReferenceSlots = chosen.Count > 0 ? chosen : [(null, null)];
        batch.Candidates = SlotCandidatesOf(rawInput);
        List<ExportSlot> composed = ComposedSlots(rawInput, slots);
        if (composed.Count == 0)
        {
            return StudioApi.Error("这个平台还没有可以出图的图位。", "invalid_slot");
        }
        expanded = [];
        foreach ((Product product, int index) in batch.Products.Select((item, index) => (item, index)))
        {
            (List<SetRules.SlotPlan> plans, JObject planError) = FullSetPlans(session, rawInput, product, composed, batch, language);
            if (planError is not null)
            {
                return planError;
            }
            foreach (SetRules.SlotPlan plan in plans.Where(plan => !plan.Covered))
            {
                batch.Uncovered.Add((index, plan.SlotId, plan.Kind, plan.Reason));
            }
            expanded.AddRange(SetRules.ExpandFullSet(1, fusionTemplateId, plans).Select(job => job with { Product = index }));
        }
        return null;
    }

    /// <summary>The slot batches' candidate count (T98): the request's number, else the server's configured default; 1–<see cref="SetRules.MaxCandidates"/>.</summary>
    private static int SlotCandidatesOf(JObject rawInput)
    {
        int asked = rawInput["candidates"]?.Type == JTokenType.Integer ? rawInput.Value<int>("candidates") : 0;
        return Math.Clamp(asked <= 0 ? SetRules.SlotCandidateCount(Program.ServerSettings.EcomCraft.SlotCandidates) : asked, 1, SetRules.MaxCandidates);
    }

    /// <summary>The slots a whole set composes (T98): the platform's non-optional slots plus the ticked optional ones, one slot per kind in slot order,
    /// non-optional first — the same composition one-click sets use (F58), so the whole set and a plain set never disagree about what exists.</summary>
    private static List<ExportSlot> ComposedSlots(JObject rawInput, List<ExportSlot> slots)
    {
        HashSet<string> seen = [];
        List<ExportSlot> composed = [];
        foreach (ExportSlot slot in slots.Where(slot => !slot.Optional).Concat(slots.Where(slot => slot.Optional)))
        {
            if (!SetRules.SetSlotKinds.Contains(slot.Kind) || slot.Optional && !TickedKinds(rawInput, slots).Contains(slot.Kind))
            {
                continue;
            }
            if (seen.Add(slot.Kind))
            {
                composed.Add(slot);
            }
        }
        return composed;
    }

    /// <summary>One product's whole-set plans (T98): every composed slot with the source that makes it for this product, or the reason it stays uncovered —
    /// the fusion slots need nothing, the detail slot needs a clear enough photo, the selling slot needs the product's selling info (T99). Nothing is
    /// invented (A20). Returns the plans, or the error when the product's photo cannot be read for the check.</summary>
    private static (List<SetRules.SlotPlan> Plans, JObject Error) FullSetPlans(Session session, JObject rawInput, Product product, List<ExportSlot> composed,
        Batch batch, string language)
    {
        (int Width, int Height)? photoSize = null;
        bool sellingFilled = false;
        bool detailFilled = false;
        bool needsPhoto = composed.Any(slot => slot.Kind == "detail");
        bool needsSelling = composed.Any(slot => slot.Kind is "selling" or "detail_page");
        if (needsPhoto)
        {
            string original = OriginalFor(session, product);
            if (original is null)
            {
                return (null, StudioApi.Error("商品原图无法读取，无法裁切细节图。", "invalid_reference"));
            }
            try
            {
                int comma = original.IndexOf(',');
                SixLabors.ImageSharp.ImageInfo identified = ISImage.Identify(Convert.FromBase64String(original[(comma + 1)..]));
                photoSize = (identified.Width, identified.Height);
            }
            catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                return (null, StudioApi.Error("商品原图无法读取，无法裁切细节图。", "invalid_reference"));
            }
        }
        if (needsSelling && product.ProductId is not null)
        {
            (Products.ProductSpecs specs, string name) = Products.ProductLibrary.SellingInfoFor(session.User.UserID, product.ProductId, language);
            sellingFilled = specs is not null && specs.Points.Count > 0 && (!string.IsNullOrEmpty(specs.DisplayName) || !string.IsNullOrEmpty(name));
            detailFilled = DetailPageRules.HasContent(specs);
        }
        List<SetRules.SlotPlan> plans = [];
        foreach (ExportSlot slot in composed)
        {
            bool detailHighResolution = photoSize is (int width, int height)
                && SetRules.DetailCropSufficient(width, height, slot.Preset.Width, slot.Preset.Height);
            string source = SetRules.FullSetSlotSource(slot.Kind, detailHighResolution, sellingFilled, detailFilled);
            bool covered = source is (SetRules.SourceFusion or SetRules.SourceCutout or SetRules.SourceCrop or SetRules.SourceLayout);
            plans.Add(covered ? new SetRules.SlotPlan(slot.Id, slot.Kind, source, null) : new SetRules.SlotPlan(slot.Id, slot.Kind, null, source));
        }
        return (plans, null);
    }

    /// <summary>The photo a product's detail crops come from (T98): the inline photo's original when the request carried one, a saved product's main-angle
    /// original from the library, or the inline photo itself; null when it cannot be read.</summary>
    private static string OriginalFor(Session session, Product product)
    {
        if (product.ProductId is null)
        {
            return product.Original ?? product.Image;
        }
        (string saved, _, string problem) = Products.ProductLibrary.ForGeneration(session.User.UserID, product.ProductId, false);
        if (saved is null)
        {
            Logs.Warning($"EcomCraft full-set could not read a product's original photo: {problem}");
        }
        return saved;
    }

    /// <summary>The display names of a platform's slots in the batch's language (T98), keyed by slot ID.</summary>
    private static Dictionary<string, string> SlotNames(string platform, string language)
    {
        JObject platformData = string.IsNullOrEmpty(platform) ? null : TemplateStore.Platforms().FirstOrDefault(item => item["id"]?.ToString() == platform);
        Dictionary<string, string> names = [];
        foreach (JObject slot in (platformData?["slots"] as JArray ?? new JArray()).OfType<JObject>())
        {
            Dictionary<string, string> slotNames = slot["name"]?.ToObject<Dictionary<string, string>>() ?? [];
            names[slot["id"]?.ToString() ?? ""] = EcomTemplate.Pick(slotNames, language);
        }
        return names;
    }

    /// <summary>The output size { width, height } of a platform slot (T98), or null when the platform or slot has none.</summary>
    private static JObject SlotSizeOf(string platform, string slotId)
    {
        JObject platformData = string.IsNullOrEmpty(platform) ? null : TemplateStore.Platforms().FirstOrDefault(item => item["id"]?.ToString() == platform);
        ExportSlot slot = (ExportRules.ParseSlots(platformData, out _) ?? []).FirstOrDefault(item => item.Id == slotId);
        return slot is null ? null : new JObject() { ["width"] = slot.Preset.Width, ["height"] = slot.Preset.Height };
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

    /// <summary>Registers the batch and starts its tasks: reference mode at once (F59, its prompts are already final), the edit trial too (F84, its prompts
    /// are written by the server), otherwise once the shared prompt is written in the background (F56).</summary>
    private static JObject SubmitBatch(Session session, Batch batch, PromptInputs inputs)
    {
        bool startsAtOnce = batch.ReferencePrompts is not null || batch.ReferenceEdit;
        foreach (Job job in batch.Jobs)
        {
            job.State = startsAtOnce ? "queued" : "prompt";
        }
        Batches[batch.Id] = batch;
        if (startsAtOnce)
        {
            // Reference prompts are already final and user-edited (F59), and the edit trial writes its own (F84): no background composition,
            // the tasks start at once.
            batch.Composed = new PromptService.Composed("", batch.ReferenceEdit ? "edit" : "reference", null, null);
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
        string tickedError = ReadTickedSlots(rawInput, slots, out HashSet<string> _);
        if (tickedError is not null)
        {
            return (null, tickedError);
        }
        List<string> kinds = [.. slots.Where(slot => !slot.Optional).Select(slot => slot.Kind).Concat(TickedKinds(rawInput, slots)).Distinct()];
        return (SetRules.SlotTemplates(references, platform, setId, kinds), null);
    }

    /// <summary>Validates the request's ticked optional slots (F58): every ticked ID must be an optional slot of the platform. Returns the error, or null.</summary>
    private static string ReadTickedSlots(JObject rawInput, List<ExportSlot> slots, out HashSet<string> ticked)
    {
        ticked = [];
        foreach (JToken token in rawInput["optional_slots"] as JArray ?? new JArray())
        {
            string id = token?.Type == JTokenType.String ? token.ToString() : "";
            if (!slots.Any(slot => slot.Id == id && slot.Optional))
            {
                return "所选的可选槽位不存在，请刷新后重新选择。";
            }
            ticked.Add(id);
        }
        return null;
    }

    /// <summary>The kinds of the optional slots the request ticked (F58), in slot order.</summary>
    private static IEnumerable<string> TickedKinds(JObject rawInput, List<ExportSlot> slots)
    {
        HashSet<string> ticked = [];
        foreach (JToken token in rawInput["optional_slots"] as JArray ?? new JArray())
        {
            if (token?.Type == JTokenType.String)
            {
                ticked.Add(token.ToString());
            }
        }
        return slots.Where(slot => slot.Optional && ticked.Contains(slot.Id)).Select(slot => slot.Kind);
    }
}
