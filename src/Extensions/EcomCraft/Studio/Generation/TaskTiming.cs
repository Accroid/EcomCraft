using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Time estimates (F70): the recent durations of each kind of operation (writing prompts, storyboards, image description, prompt enhancement)
/// and the per-image generation time by workflow, size and execution server. Everything is kept in memory only and holds seconds, never prompts,
/// images or other content; a restart starts over. Generations are also tracked by claim ID while they run, so the queue can tell their owner how long
/// they still take.</summary>
[API.APIClass("EcomCraft time estimates: typical durations of prompt operations for the waiting views.")]
public static class TaskTiming
{
    /// <summary>Kind: the prompt written in the background for a submission or a set (F56).</summary>
    public const string Compose = "compose";

    /// <summary>Kind: the AI expansion of the simple-mode prompt panel (F54).</summary>
    public const string Expand = "expand";

    /// <summary>Kind: selling points and storyboard (F25).</summary>
    public const string Storyboard = "storyboard";

    /// <summary>Kind: describing one reference image with the vision model (F59).</summary>
    public const string Describe = "describe";

    /// <summary>Kind: the professional-mode prompt enhancement.</summary>
    public const string Enhance = "enhance";

    /// <summary>Most duration windows kept, so unusual workflow and size combinations cannot grow the table without bound.</summary>
    private const int MaxWindows = 500;

    /// <summary>How long a tracked generation is kept when its end was never seen.</summary>
    private const long TrackedKeepMs = 6 * 3600 * 1000;

    /// <summary>Duration windows by kind or image key.</summary>
    private static readonly ConcurrentDictionary<string, DurationRules.Window> Windows = new();

    /// <summary>One scheduled generation: the execution server, the workflow and size key, the tasks queued ahead of it at submit,
    /// its own image count and when it was submitted (<see cref="Environment.TickCount64"/>).</summary>
    public sealed record class Scheduled(int ServerId, string WorkflowKey, int Ahead, int Images, long Submitted);

    /// <summary>Running background generations by claim ID, so the queue can add their estimate for the owner.</summary>
    private static readonly ConcurrentDictionary<long, Scheduled> ByClaim = new();

    /// <summary>Called with the generation scheduled in the current async flow; a set task sets it to learn its estimate while it waits for the result.</summary>
    public static readonly AsyncLocal<Action<Scheduled>> Observer = new();

    /// <summary>Records one duration of a kind of operation.</summary>
    public static void Record(string kind, double seconds)
    {
        if (!Windows.ContainsKey(kind) && Windows.Count >= MaxWindows)
        {
            return;
        }
        DurationRules.Window window = Windows.GetOrAdd(kind, _ => new DurationRules.Window());
        lock (window)
        {
            window.Add(seconds);
        }
    }

    /// <summary>Records the time since <paramref name="started"/> (<see cref="Environment.TickCount64"/>) for a kind of operation.</summary>
    public static void RecordSince(string kind, long started) => Record(kind, (Environment.TickCount64 - started) / 1000.0);

    /// <summary>How long a kind of operation typically takes in seconds, or null before the first run.</summary>
    public static double? Typical(string kind)
    {
        if (!Windows.TryGetValue(kind, out DurationRules.Window window))
        {
            return null;
        }
        lock (window)
        {
            return window.Typical();
        }
    }

    /// <summary>The image key of a workflow and size on a server.</summary>
    private static string ImageKey(int serverId, string workflowKey) => $"image:{serverId}:{workflowKey}";

    /// <summary>The image key of a server, for workflows and sizes it has not run yet.</summary>
    private static string ImageKey(int serverId) => $"image:{serverId}";

    /// <summary>Typical seconds per image of a scheduled generation: by its workflow and size on its server, else by the server, else null.</summary>
    public static double? PerImage(Scheduled scheduled) => Typical(ImageKey(scheduled.ServerId, scheduled.WorkflowKey)) ?? Typical(ImageKey(scheduled.ServerId));

    /// <summary>Records a successful generation's per-image time for its workflow and size and for its server.</summary>
    public static void Finished(Scheduled scheduled)
    {
        double wall = (Environment.TickCount64 - scheduled.Submitted) / 1000.0;
        double perImage = DurationRules.PerImage(wall, scheduled.Ahead, scheduled.Images);
        Record(ImageKey(scheduled.ServerId, scheduled.WorkflowKey), perImage);
        Record(ImageKey(scheduled.ServerId), perImage);
    }

    /// <summary>Tracks a background generation by its claim ID until <see cref="Forget"/>; entries whose end was never seen expire.</summary>
    public static void Watch(long claimId, Scheduled scheduled)
    {
        long cutoff = Environment.TickCount64 - TrackedKeepMs;
        foreach (KeyValuePair<long, Scheduled> old in ByClaim.Where(pair => pair.Value.Submitted < cutoff).ToArray())
        {
            ByClaim.TryRemove(old.Key, out _);
        }
        ByClaim[claimId] = scheduled;
    }

    /// <summary>Stops tracking a background generation.</summary>
    public static void Forget(long claimId) => ByClaim.TryRemove(claimId, out _);

    /// <summary>The tracked generation of a claim, or null.</summary>
    public static Scheduled ForClaim(long claimId) => ByClaim.TryGetValue(claimId, out Scheduled scheduled) ? scheduled : null;

    /// <summary>Adds the estimate of a scheduled generation for its owner: elapsed_seconds, estimate_seconds (the whole wait and run), remaining_seconds
    /// and, unless it is known to be running, start_in_seconds (until the tasks ahead of it are done). Estimates are null before the server has a sample.</summary>
    public static void AddEstimate(JObject target, Scheduled scheduled, bool running)
    {
        double elapsed = Math.Max(0, (Environment.TickCount64 - scheduled.Submitted) / 1000.0);
        double? perImage = PerImage(scheduled);
        double? total = perImage * (Math.Max(0, scheduled.Ahead) + Math.Max(1, scheduled.Images));
        target["elapsed_seconds"] = (int)elapsed;
        target["estimate_seconds"] = total is double value ? (int)Math.Round(value) : null;
        target["remaining_seconds"] = DurationRules.Remaining(total, elapsed);
        target["start_in_seconds"] = running || scheduled.Ahead <= 0 ? null : DurationRules.Remaining(perImage * scheduled.Ahead, elapsed);
    }

    /// <summary>Adds the estimate of a prompt being written since <paramref name="started"/> (<see cref="Environment.TickCount64"/>), with the given field prefix.</summary>
    public static void AddPromptEstimate(JObject target, long started, string prefix = "")
    {
        double elapsed = Math.Max(0, (Environment.TickCount64 - started) / 1000.0);
        double? typical = Typical(Compose);
        target[$"{prefix}elapsed_seconds"] = (int)elapsed;
        target[$"{prefix}estimate_seconds"] = typical is double value ? (int)Math.Round(value) : null;
        target[$"{prefix}remaining_seconds"] = DurationRules.Remaining(typical, elapsed);
    }

    /// <summary>Rounded typical seconds of a kind, or null.</summary>
    private static int? Seconds(string kind) => Typical(kind) is double value ? (int)Math.Round(value) : null;

    [API.APIDescription("Typical durations in seconds of the operations the workspace waits for (F70): compose (the prompt written for a submission), expand, storyboard, "
        + "describe (one reference image) and enhance; null before the first run since the server started. Durations only, never content.",
        "compose: 8, expand: 6, storyboard: 20, describe: 12, enhance: null")]
    public static Task<JObject> EcomCraftStudioEstimates(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        return Task.FromResult(new JObject()
        {
            [Compose] = Seconds(Compose),
            [Expand] = Seconds(Expand),
            [Storyboard] = Seconds(Storyboard),
            [Describe] = Seconds(Describe),
            [Enhance] = Seconds(Enhance)
        });
    }
}
