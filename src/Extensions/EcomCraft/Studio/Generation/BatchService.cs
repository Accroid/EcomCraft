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
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>One-click sets and batches (F03): product photos × the set's templates become independent tasks (R5.7). Each task goes through the normal template
/// generation, so it is checked against the snapshot allowlist and scheduled by Rule 1 on its own; tasks of different people run in parallel on several servers,
/// while each person runs at most a few at once (R9.4). Batches live in memory for six hours (up to a day while tasks are unfinished) and only their owner can see them.
/// This file holds the data types, the API routes and the batch lifecycle; StartSet lives in BatchService.Start.cs and the task run in BatchService.Run.cs (R10).</summary>
[API.APIClass("EcomCraft one-click sets and batches: expand products and a platform's image set into tasks, follow them, retry or cancel.")]
public static partial class BatchService
{
    /// <summary>Interval of the background loop that drops expired batches.</summary>
    private const int CleanupLoopMs = 10 * 60 * 1000;

    /// <summary>Stops the background loop when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

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
        public List<string> ReferencePrompts;

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

    /// <summary>Starts the background loop that drops expired batches, so their images do not wait in memory for the next set to be started.</summary>
    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            while (!StopSource.IsCancellationRequested && !Program.GlobalProgramCancel.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CleanupLoopMs, StopSource.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                try
                {
                    Cleanup();
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft batch cleanup loop failed: {ex.Message}");
                }
            }
        });
    }

    /// <summary>Stops the loop.</summary>
    public static void Shutdown()
    {
        StopSource.Cancel();
    }

    /// <summary>Whether a batch still has tasks that have not finished.</summary>
    private static bool HasUnfinishedTasks(Batch batch)
    {
        lock (batch.Lock)
        {
            return batch.Jobs.Any(job => job.State is "prompt" or "queued" or "running");
        }
    }

    /// <summary>Drops expired batches.</summary>
    private static void Cleanup()
    {
        long now = Environment.TickCount64;
        foreach (KeyValuePair<string, Batch> pair in Batches.ToArray())
        {
            if (SetRules.BatchExpired(now - pair.Value.Created, HasUnfinishedTasks(pair.Value)))
            {
                Batches.TryRemove(pair.Key, out _);
            }
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
