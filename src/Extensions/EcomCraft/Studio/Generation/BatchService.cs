using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Export;
using EcomCraftStudio.Studio.PostProcess;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Accounts;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>One-click sets and batches (F03): product photos × the set's templates become independent tasks (R5.7). Each task goes through the normal template
/// generation, so it is checked against the snapshot allowlist and scheduled by Rule 1 on its own; tasks of different people run in parallel on several servers,
/// while each person runs at most a few at once (R9.4). Batches live in memory for six hours and only their owner can see them.</summary>
[API.APIClass("EcomCraft one-click sets and batches: expand products and a platform's image set into tasks, follow them, retry or cancel.")]
public static class BatchService
{
    /// <summary>How long a batch stays readable.</summary>
    private const long KeepMs = 6 * 60 * 60 * 1000;

    /// <summary>Largest product photo, as a data URL.</summary>
    private const int MaxImageDataLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>One product photo of a batch, with its cut-out once made and the placement used for product preservation.</summary>
    private sealed class Product
    {
        /// <summary>The photo as a data URL.</summary>
        public string Image;

        /// <summary>The cut-out as a data URL, made on first need.</summary>
        public string Cutout;

        /// <summary>Why the cut-out could not be made, if it failed.</summary>
        public string CutoutError;

        /// <summary>Where the product sits for preserving templates.</summary>
        public Placement Placement = Placement.Default;

        /// <summary>Serializes making the cut-out.</summary>
        public readonly SemaphoreSlim CutoutLock = new(1, 1);
    }

    /// <summary>One task of a batch, as reported to its owner.</summary>
    public sealed class Job
    {
        /// <summary>Task number inside the batch.</summary>
        public int Id;

        /// <summary>Index of the product photo.</summary>
        public int Product;

        /// <summary>Template ID.</summary>
        public string Template;

        /// <summary>Template name in the batch's language.</summary>
        public string TemplateName;

        /// <summary>The storyboard shot this task makes (F25), or -1 for an ordinary set.</summary>
        public int Shot = -1;

        /// <summary>The shot's purpose as the user confirmed it (F25), shown instead of the template name; null for an ordinary set.</summary>
        public string Purpose;

        /// <summary>The reference image this task makes (F59), or -1 for an ordinary set; the page shows that reference's thumbnail.</summary>
        public int Reference = -1;

        /// <summary>"prompt" (waiting for the batch prompt, F56), "queued", "running", "done", "failed" or "cancelled".</summary>
        public string State = "queued";

        /// <summary>User-facing failure reason.</summary>
        public string Error;

        /// <summary>Failure code for translation.</summary>
        public string ErrorId;

        /// <summary>Display name of the server that ran it.</summary>
        public string Server;

        /// <summary>Result URLs.</summary>
        public List<string> Images = [];

        /// <summary>The scheduled generation while the task waits for its result, for the time estimate (F70); null otherwise.</summary>
        public TaskTiming.Scheduled Timing;
    }

    /// <summary>One batch: owner, products, tasks and the shared generation settings.</summary>
    private sealed class Batch
    {
        /// <summary>Batch ID.</summary>
        public string Id;

        /// <summary>Owner account.</summary>
        public string UserId;

        /// <summary>Creation time, <see cref="Environment.TickCount64"/>.</summary>
        public long Created;

        /// <summary>Product photos.</summary>
        public List<Product> Products = [];

        /// <summary>Tasks.</summary>
        public List<Job> Jobs = [];

        /// <summary>Prompt composed once for the whole batch; null until the background composition finished (F56).</summary>
        public string Prompt;

        /// <summary>The composition result for the owner's result card and notices (F54/F56); null until the prompt is ready.</summary>
        public PromptService.Composed Composed;

        /// <summary>English prompt fragment of each storyboard shot (F25), added to <see cref="Prompt"/> for that shot's tasks; Chinese fragments are translated with the prompt.</summary>
        public List<string> ShotPrompts = [];

        /// <summary>Purpose of each storyboard shot (F25).</summary>
        public List<string> ShotPurposes = [];

        /// <summary>English prompt of each reference image (F59), already final and user-edited: reference mode does no background composition.</summary>
        public List<string> ReferencePrompts = [];

        /// <summary>Product placement of each reference (F59, T79), or null where the product's own placement applies.</summary>
        public List<Placement> ReferencePlacements = [];

        /// <summary>Reference images of reference mode (F59), kept in memory only for the batch's lifetime; never written under Data/.</summary>
        public List<string> ReferenceImages = [];

        /// <summary>Whether some shot fragment could not be translated and was left out (F25).</summary>
        public bool ShotsUntranslated;

        /// <summary>The owner's brand kit for every task (F12), or null.</summary>
        public string BrandKitId;

        /// <summary>Optional user negative prompt.</summary>
        public string Negative;

        /// <summary>Candidates per task.</summary>
        public int Candidates;

        /// <summary>Optional pinned execution server.</summary>
        public int? Server;

        /// <summary>Target platform, so main-image templates can take the cut-out-only branch.</summary>
        public string Platform = "";

        /// <summary>The output size chosen before generating, as { width, height } for every task; null where each task takes the platform's slot size for its image kind.</summary>
        public JObject OutputSize;

        /// <summary>Cancels tasks still waiting; replaced when a task is retried after cancelling.</summary>
        public CancellationTokenSource Cancel = new();

        /// <summary>Protects the task states.</summary>
        public readonly object Lock = new();
    }

    /// <summary>Batches by ID.</summary>
    private static readonly ConcurrentDictionary<string, Batch> Batches = new();

    /// <summary>Per-person limit of running tasks (R9.4).</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Slots = new();

    [API.APIDescription("Start a one-click set or batch: products (1–20, each { image: data URL, placement?: { x, y, size } }) × the templates of set_id (default \"standard\") that suit platform. Answers at once with the batch ID and its tasks in state \"prompt\"; the prompt is composed once in the background from the prompt panel (prompt_en / prompt_zh) or style_id and description, and the tasks then start (F56). rawInput is the root request JSON.", "batch_id: \"…\", jobs: []")]
    public static Task<JObject> EcomCraftStudioGenerateSet(Session session, JObject rawInput) => Task.FromResult(StartSet(session, rawInput));

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
                if (image is null || image.Length > MaxImageDataLength || !Regex.IsMatch(image[..Math.Min(image.Length, 30)], "^data:image/(png|jpeg|webp);base64,"))
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
        string language = rawInput.Value<string>("language") == "en" ? "en" : "zh-CN";
        string platform = rawInput["platform"]?.Type == JTokenType.String ? rawInput.Value<string>("platform") : "";
        string setId = rawInput["set_id"]?.Type == JTokenType.String ? rawInput.Value<string>("set_id") : "standard";
        int candidates = rawInput["candidates"]?.Type == JTokenType.Integer ? rawInput.Value<int>("candidates") : 4;
        if (candidates < 1 || candidates > 20)
        {
            return StudioApi.Error("每种图片的候选张数须为 1–20。", "invalid_parameters");
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
        List<string> referencePrompts = null;
        if (rawInput["reference_prompts"] is JArray promptItems)
        {
            if (promptItems.Count == 0 || promptItems.Count > SetRules.MaxReferences)
            {
                return StudioApi.Error($"参考图须为 1–{SetRules.MaxReferences} 张。", "invalid_parameters");
            }
            if (rawInput["shots"] is JArray withShots && withShots.Count > 0)
            {
                return StudioApi.Error("分镜与参考图不能同时使用，请二选一。", "invalid_parameters");
            }
            referencePrompts = [];
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
                referencePrompts.Add(cleaned);
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
                    if (!image.StartsWith("data:image/", StringComparison.Ordinal) || image.Length > MaxImageDataLength)
                    {
                        return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
                    }
                    batch.ReferenceImages.Add(image);
                }
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
            batch.ReferencePrompts = referencePrompts;
            batch.Candidates = 1;
            candidates = 1;
            expanded = SetRules.ExpandReferences(batch.Products.Count, scene.Id, referencePrompts.Count);
        }
        else if (rawInput["shots"] is JArray shotItems && shotItems.Count > 0)
        {
            // A confirmed storyboard (F25): every shot is checked again, since the page may have changed it; only template IDs and cleaned text are kept.
            if (shotItems.Count > StoryboardRules.MaxShots)
            {
                return StudioApi.Error($"分镜最多 {StoryboardRules.MaxShots} 张。", "invalid_parameters");
            }
            List<string> shotTemplates = [];
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
        // One shared cap for single generations and whole sets alike (F55): products × set types × candidates may not exceed 20 images.
        if (expanded.Count * candidates > 20)
        {
            return StudioApi.Error($"预计 {expanded.Count * candidates} 张，超过 20 张总上限，请减少商品图、类型或候选张数。", "invalid_parameters");
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
        string description = "";
        string style = "";
        string productTypeId = "";
        bool needModel = false;
        string promptEn = "";
        string promptZh = "";
        // In reference mode none of the other step-2 options apply (F59): no description, style, product type, prompt panel or brand kit.
        if (referencePrompts is null)
        {
            description = rawInput["description"]?.Type == JTokenType.String ? rawInput.Value<string>("description").Trim() : "";
            if (description.Length > PromptService.MaxDescriptionLength)
            {
                return StudioApi.Error($"描述最多 {PromptService.MaxDescriptionLength} 个字符。", "invalid_prompt");
            }
            style = rawInput["style_id"]?.Type == JTokenType.String ? rawInput.Value<string>("style_id") : "";
            productTypeId = rawInput["product_type_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_type_id") : "";
            needModel = rawInput["need_model"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("need_model");
            if (!PromptService.KnownProductType(productTypeId))
            {
                return StudioApi.Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
            }
            promptEn = rawInput["prompt_en"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_en") : "";
            promptZh = rawInput["prompt_zh"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_zh") : "";
            if (promptEn.Length > PromptRules.MaxPromptLength || promptZh.Length > PromptRules.MaxPromptLength)
            {
                return StudioApi.Error($"提示词最多 {PromptRules.MaxPromptLength} 个字符。", "invalid_prompt");
            }
            if (rawInput["brand_kit_id"]?.Type == JTokenType.String && rawInput.Value<string>("brand_kit_id").Length > 0)
            {
                // Checked now so a missing kit fails at once; every task then sends the kit's ID through the normal template path (F12).
                (_, string brandProblem) = Brand.BrandKitLibrary.FragmentFor(session.User.UserID, rawInput.Value<string>("brand_kit_id"));
                if (brandProblem is not null)
                {
                    return StudioApi.Error(brandProblem, "invalid_brand_kit");
                }
                batch.BrandKitId = rawInput.Value<string>("brand_kit_id");
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
        foreach (Job job in batch.Jobs)
        {
            job.State = referencePrompts is null ? "prompt" : "queued";
        }
        Batches[batch.Id] = batch;
        if (referencePrompts is not null)
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
            PromptService.Composed composed = await PromptService.ComposePanelAsync(session, style, productTypeId, needModel, description, promptEn, promptZh);
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

    /// <summary>Runs one task once a slot of its owner is free.</summary>
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

    private static async Task RunJobAsync(Session session, Batch batch, Job job)
    {
        SemaphoreSlim slot = Slots.GetOrAdd(batch.UserId, _ => new SemaphoreSlim(SetRules.Concurrency(session.User.CalcMaxT2ISimultaneous)));
        CancellationToken cancel;
        lock (batch.Lock)
        {
            cancel = batch.Cancel.Token;
        }
        try
        {
            await slot.WaitAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            SetState(batch, job, "cancelled", null, null);
            return;
        }
        try
        {
            if (cancel.IsCancellationRequested)
            {
                SetState(batch, job, "cancelled", null, null);
                return;
            }
            SetState(batch, job, "running", null, null);
            // This task's own async flow learns the scheduled generation, so the status can show how long it still takes (F70).
            TaskTiming.Observer.Value = timing =>
            {
                lock (batch.Lock)
                {
                    job.Timing = timing;
                }
            };
            JObject result = await GenerateAsync(session, batch, job);
            lock (batch.Lock)
            {
                job.Timing = null;
            }
            if (result.ContainsKey("error"))
            {
                SetState(batch, job, "failed", result["error"]?.ToString(), result["error_id"]?.ToString());
                return;
            }
            lock (batch.Lock)
            {
                job.Server = result["execution_server"]?.ToString();
                job.Images = [.. (result["images"] as JArray ?? new JArray()).Select(image => image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString())
                    .Where(url => url is not null && (url.StartsWith("View/", StringComparison.Ordinal) || url.StartsWith("Output/", StringComparison.Ordinal)))];
            }
            // The engine can end without an error yet without images (an image task that threw is only logged); that is a failure, not a finished task.
            if (job.Images.Count == 0)
            {
                Logs.Warning($"EcomCraft batch task ended without images (template {job.Template}).");
                SetState(batch, job, "failed", "执行服务器没有返回图片，请重试；仍然失败请把服务器日志发给管理员。", "generation_failed");
                return;
            }
            SetState(batch, job, "done", null, null);
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft batch task failed: {ex.GetType().Name}");
            SetState(batch, job, "failed", "任务没有完成，请重试。", "generation_uncertain");
        }
        finally
        {
            slot.Release();
        }
    }

    /// <summary>Builds the template request for one task and runs it through the normal generation path (synchronously, so results come back here).</summary>
    private static async Task<JObject> GenerateAsync(Session session, Batch batch, Job job)
    {
        (EcomTemplate Template, string WorkflowId, string ImageField) template = TemplateApi.RunnableTemplates(session).FirstOrDefault(item => item.Template.Id == job.Template);
        if (template.Template is null)
        {
            return StudioApi.Error("这个模板暂时不可用：执行服务器可能离线，或模板已被下架。", "no_execution_server");
        }
        Product product = batch.Products[job.Product];
        JObject parameters = new();
        if (template.ImageField is not null)
        {
            string image = product.Image;
            if (template.Template.PreserveProduct)
            {
                image = await CutoutFor(product);
                if (image is null)
                {
                    return StudioApi.Error(product.CutoutError ?? "抠图没有完成。", "cutout_failed");
                }
            }
            parameters[template.ImageField] = image;
        }
        Placement placement = job.Reference >= 0 && job.Reference < batch.ReferencePlacements.Count && batch.ReferencePlacements[job.Reference] is Placement forReference
            ? forReference : product.Placement;
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = parameters,
            ["prompt"] = job.Reference >= 0 && job.Reference < batch.ReferencePrompts.Count ? batch.ReferencePrompts[job.Reference]
                : job.Shot >= 0 && job.Shot < batch.ShotPrompts.Count && batch.ShotPrompts[job.Shot].Length > 0
                ? string.Join(", ", new[] { batch.Prompt, batch.ShotPrompts[job.Shot] }.Where(part => !string.IsNullOrEmpty(part)))
                : batch.Prompt,
            ["negativeprompt"] = batch.Negative,
            ["images"] = batch.Candidates,
            ["placement"] = new JObject() { ["x"] = placement.CenterX, ["y"] = placement.CenterY, ["size"] = placement.Size },
            ["platform"] = batch.Platform
        };
        if (batch.OutputSize is not null)
        {
            payload["output_size"] = batch.OutputSize.DeepClone();
        }
        if (job.Reference >= 0)
        {
            // A reference task's prompt is final and user-edited (F59): it is sent as is, without the template's own fragments.
            payload["raw_prompt"] = true;
        }
        if (batch.Server is int server)
        {
            payload["execution_server_id"] = server;
        }
        if (batch.BrandKitId is not null)
        {
            payload["brand_kit_id"] = batch.BrandKitId;
        }
        return await StudioApi.EcomCraftStudioGenerate(session, payload);
    }

    /// <summary>The product's cut-out: the photo itself when it is already transparent, otherwise made once on an execution server. Null when it failed.</summary>
    private static async Task<string> CutoutFor(Product product)
    {
        await product.CutoutLock.WaitAsync();
        try
        {
            if (product.Cutout is not null || product.CutoutError is not null)
            {
                return product.Cutout;
            }
            byte[] bytes = ProductComposite.FitForWork(Convert.FromBase64String(product.Image[(product.Image.IndexOf(',') + 1)..]));
            if (bytes is null)
            {
                product.CutoutError = "商品图尺寸过大，请缩小到 1 亿像素以内。";
                return null;
            }
            using (Image<Rgba32> image = ISImage.Load<Rgba32>(bytes))
            {
                if (ProductComposite.HasTransparency(image))
                {
                    product.Cutout = product.Image;
                    return product.Cutout;
                }
            }
            (byte[] png, _, string error, _) = await CutoutService.CutoutAsync(bytes);
            if (png is null)
            {
                product.CutoutError = error;
                return null;
            }
            product.Cutout = "data:image/png;base64," + Convert.ToBase64String(png);
            return product.Cutout;
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            product.CutoutError = "无法读取商品图，请重新导出为 PNG、JPEG 或 WebP。";
            return null;
        }
        finally
        {
            product.CutoutLock.Release();
        }
    }

    /// <summary>Updates a task under the batch lock.</summary>
    private static void SetState(Batch batch, Job job, string state, string error, string errorId)
    {
        lock (batch.Lock)
        {
            job.State = state;
            job.Error = error;
            job.ErrorId = errorId;
        }
    }

    /// <summary>One task as its owner sees it; a task waiting for its generation also carries its time estimate (F70). Callers hold the batch lock.</summary>
    private static JObject JobStatus(Job job)
    {
        JObject status = new()
        {
            ["id"] = job.Id,
            ["product"] = job.Product,
            ["template"] = job.Template,
            ["template_name"] = job.TemplateName,
            ["purpose"] = job.Purpose,
            ["reference_index"] = job.Reference,
            ["state"] = job.State,
            ["error"] = job.Error,
            ["error_id"] = job.ErrorId,
            ["execution_server"] = job.Server,
            ["images"] = new JArray(job.Images)
        };
        if (job.Timing is not null && job.State == "running")
        {
            // Set tasks wait for their result in one call, so whether the server already runs them is not known here.
            TaskTiming.AddEstimate(status, job.Timing, false);
        }
        return status;
    }

    /// <summary>The batch as its owner sees it. Once the prompt is ready the owner's own answer also carries the prompt used and any fallback notice (F54/F56);
    /// batches are only ever readable by their owner.</summary>
    private static JObject Status(Batch batch)
    {
        lock (batch.Lock)
        {
            JObject status = new()
            {
                ["prompt_ready"] = batch.Composed is not null,
                ["batch_id"] = batch.Id,
                ["products"] = batch.Products.Count,
                ["jobs"] = new JArray(batch.Jobs.Select(JobStatus))
            };
            if (batch.Composed is null)
            {
                // The batch prompt is still being written (F56): how long it usually takes (F70).
                TaskTiming.AddPromptEstimate(status, batch.Created, "prompt_");
            }
            if (batch.Composed is PromptService.Composed composed)
            {
                status["prompt_used"] = composed.Prompt;
                status["shots_untranslated"] = batch.ShotsUntranslated;
                if (composed.Translated is not null)
                {
                    status["prompt_translated"] = composed.Translated;
                }
                status["prompt_notice"] = composed.Notice;
                status["prompt_notice_code"] = composed.NoticeCode;
            }
            return status;
        }
    }

    /// <summary>The owner's batch, or null.</summary>
    private static Batch Find(Session session, string batchId) =>
        batchId is not null && Batches.TryGetValue(batchId, out Batch batch) && batch.UserId == session.User.UserID ? batch : null;

    /// <summary>Drops expired batches.</summary>
    private static void Cleanup()
    {
        long cutoff = Environment.TickCount64 - KeepMs;
        foreach (KeyValuePair<string, Batch> pair in Batches.Where(pair => pair.Value.Created < cutoff).ToArray())
        {
            Batches.TryRemove(pair.Key, out _);
        }
    }

    [API.APIDescription("Read the tasks of one of the current account's batches. While the prompt is written the batch carries prompt_elapsed_seconds, "
        + "prompt_estimate_seconds and prompt_remaining_seconds, and a running task carries elapsed_seconds, estimate_seconds, remaining_seconds and start_in_seconds (F70).", "batch_id: \"…\", jobs: []")]
    public static Task<JObject> EcomCraftStudioBatchStatus(Session session, string batch_id)
    {
        Batch batch = StudioApi.CanView(session) ? Find(session, batch_id) : null;
        return Task.FromResult(batch is null ? StudioApi.Error("找不到这个批次，可能已经过期。", "task_not_found") : Status(batch));
    }

    [API.APIDescription("Retry one failed or cancelled task of the current account's batch.", "batch_id: \"…\", jobs: []")]
    public static Task<JObject> EcomCraftStudioBatchRetry(Session session, string batch_id, int job_id)
    {
        Batch batch = StudioApi.CanView(session) && session.User.HasPermission(Permissions.BasicImageGeneration) ? Find(session, batch_id) : null;
        Job job = batch?.Jobs.FirstOrDefault(item => item.Id == job_id);
        if (job is null)
        {
            return Task.FromResult(StudioApi.Error("找不到这个任务，可能已经过期。", "task_not_found"));
        }
        lock (batch.Lock)
        {
            if (job.State is not ("failed" or "cancelled"))
            {
                return Task.FromResult(StudioApi.Error("只有失败或已取消的任务可以重试。", "workflow_state"));
            }
            if (batch.Cancel.IsCancellationRequested)
            {
                batch.Cancel.Dispose();
                batch.Cancel = new CancellationTokenSource();
            }
            job.Error = null;
            job.ErrorId = null;
            Product product = batch.Products[job.Product];
            if (product.CutoutError is not null)
            {
                product.CutoutError = null;
            }
            // Cancelled while the prompt was still being written (F56): the task starts with the others once the prompt is ready.
            if (batch.Composed is null)
            {
                job.State = "prompt";
                return Task.FromResult(Status(batch));
            }
            job.State = "queued";
        }
        _ = Utilities.RunCheckedTask(() => RunJobAsync(session, batch, job), "ecomcraft batch retry");
        return Task.FromResult(Status(batch));
    }

    [API.APIDescription("Cancel the tasks of the current account's batch that are still waiting; running tasks finish.", "batch_id: \"…\", jobs: []")]
    public static Task<JObject> EcomCraftStudioBatchCancel(Session session, string batch_id)
    {
        Batch batch = StudioApi.CanView(session) ? Find(session, batch_id) : null;
        if (batch is null)
        {
            return Task.FromResult(StudioApi.Error("找不到这个批次，可能已经过期。", "task_not_found"));
        }
        lock (batch.Lock)
        {
            batch.Cancel.Cancel();
            foreach (Job job in batch.Jobs.Where(job => job.State is "queued" or "prompt"))
            {
                job.State = "cancelled";
            }
        }
        return Task.FromResult(Status(batch));
    }
}
