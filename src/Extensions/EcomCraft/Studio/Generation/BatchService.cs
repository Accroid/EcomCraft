using System.Text.RegularExpressions;
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

        /// <summary>Optional user negative prompt.</summary>
        public string Negative;

        /// <summary>Candidates per task.</summary>
        public int Candidates;

        /// <summary>Optional pinned execution server.</summary>
        public int? Server;

        /// <summary>Target platform, so main-image templates can take the cut-out-only branch.</summary>
        public string Platform = "";

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
        List<SetRules.TemplateRef> setTemplates = SetRules.SetTemplates(runnable.Select(item => new SetRules.TemplateRef(item.Template.Id, item.Template.Platforms, item.Template.Sets)), platform, setId);
        if (setTemplates.Count == 0)
        {
            return StudioApi.Error("这个平台暂时没有可用的套图模板。请管理员发布模板，或确认执行服务器在线。", "invalid_template");
        }
        List<SetRules.Job> expanded = SetRules.Expand(batch.Products.Count, setTemplates);
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
            batch.Jobs.Add(new Job() { Id = batch.Jobs.Count, Product = job.Product, Template = job.Template, TemplateName = EcomTemplate.Pick(template.Name, language) });
        }
        string description = rawInput["description"]?.Type == JTokenType.String ? rawInput.Value<string>("description").Trim() : "";
        if (description.Length > PromptService.MaxDescriptionLength)
        {
            return StudioApi.Error($"描述最多 {PromptService.MaxDescriptionLength} 个字符。", "invalid_prompt");
        }
        string style = rawInput["style_id"]?.Type == JTokenType.String ? rawInput.Value<string>("style_id") : "";
        string productTypeId = rawInput["product_type_id"]?.Type == JTokenType.String ? rawInput.Value<string>("product_type_id") : "";
        bool needModel = rawInput["need_model"]?.Type == JTokenType.Boolean && rawInput.Value<bool>("need_model");
        if (!PromptService.KnownProductType(productTypeId))
        {
            return StudioApi.Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
        }
        string promptEn = rawInput["prompt_en"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_en") : "";
        string promptZh = rawInput["prompt_zh"]?.Type == JTokenType.String ? rawInput.Value<string>("prompt_zh") : "";
        if (promptEn.Length > PromptRules.MaxPromptLength || promptZh.Length > PromptRules.MaxPromptLength)
        {
            return StudioApi.Error($"提示词最多 {PromptRules.MaxPromptLength} 个字符。", "invalid_prompt");
        }
        batch.Negative = rawInput["negativeprompt"]?.Type == JTokenType.String ? rawInput.Value<string>("negativeprompt") : "";
        foreach (Job job in batch.Jobs)
        {
            job.State = "prompt";
        }
        Batches[batch.Id] = batch;
        // The batch is answered at once; the prompt is composed once in the background (F56) and the tasks start when it is ready.
        // The bilingual prompt panel (F54) decides the prompt when it holds text; otherwise the one-line description is composed as before.
        _ = Utilities.RunCheckedTask(async () =>
        {
            PromptService.Composed composed = await PromptService.ComposePanelAsync(session, style, productTypeId, needModel, description, promptEn, promptZh);
            List<Job> ready;
            lock (batch.Lock)
            {
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
            JObject result = await GenerateAsync(session, batch, job);
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
                job.State = "done";
            }
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
        JObject payload = new()
        {
            ["source"] = "local",
            ["template_id"] = template.Template.Id,
            ["template_params"] = parameters,
            ["prompt"] = batch.Prompt,
            ["negativeprompt"] = batch.Negative,
            ["images"] = batch.Candidates,
            ["placement"] = new JObject() { ["x"] = product.Placement.CenterX, ["y"] = product.Placement.CenterY, ["size"] = product.Placement.Size },
            ["platform"] = batch.Platform
        };
        if (batch.Server is int server)
        {
            payload["execution_server_id"] = server;
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
            byte[] bytes = Convert.FromBase64String(product.Image[(product.Image.IndexOf(',') + 1)..]);
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
                ["jobs"] = new JArray(batch.Jobs.Select(job => new JObject()
                {
                    ["id"] = job.Id,
                    ["product"] = job.Product,
                    ["template"] = job.Template,
                    ["template_name"] = job.TemplateName,
                    ["state"] = job.State,
                    ["error"] = job.Error,
                    ["error_id"] = job.ErrorId,
                    ["execution_server"] = job.Server,
                    ["images"] = new JArray(job.Images)
                }))
            };
            if (batch.Composed is PromptService.Composed composed)
            {
                status["prompt_used"] = composed.Prompt;
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

    [API.APIDescription("Read the tasks of one of the current account's batches.", "batch_id: \"…\", jobs: []")]
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
