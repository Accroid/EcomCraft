namespace EcomCraftStudio.Studio.Generation;

/// <summary>Short-lived results of background generations, so the submitting browser learns whether a task failed and can offer reassignment (R6).
/// Holds only IDs, the server display name and an already sanitized message: never prompts, parameters, images or graphs.</summary>
public static class TaskOutcomes
{
    /// <summary>How long a result stays readable.</summary>
    private const long KeepMs = 15 * 60 * 1000;

    /// <summary>Upper bound for results kept per account.</summary>
    private const int MaxPerUser = 50;

    /// <summary>One finished background task. <see cref="TaskId"/> is the SwarmUI generation claim ID, unique across sessions.</summary>
    public sealed record class Outcome(string UserId, long TaskId, bool Failed, string Error, string ErrorId, string Server, long Finished);

    /// <summary>Results by task ID.</summary>
    private static readonly ConcurrentDictionary<long, Outcome> Outcomes = new();

    /// <summary>Records a finished task and drops expired or excess results of the same account.</summary>
    public static void Record(Outcome outcome)
    {
        if (outcome is null || outcome.TaskId <= 0 || string.IsNullOrEmpty(outcome.UserId))
        {
            return;
        }
        Outcomes[outcome.TaskId] = outcome;
        long cutoff = Environment.TickCount64 - KeepMs;
        foreach (Outcome old in Outcomes.Values.Where(item => item.Finished < cutoff).ToArray())
        {
            Outcomes.TryRemove(old.TaskId, out _);
        }
        foreach (Outcome excess in Outcomes.Values.Where(item => item.UserId == outcome.UserId).OrderByDescending(item => item.Finished).Skip(MaxPerUser).ToArray())
        {
            Outcomes.TryRemove(excess.TaskId, out _);
        }
    }

    /// <summary>The account's unexpired results, newest first.</summary>
    public static List<Outcome> ForUser(string userId)
    {
        long cutoff = Environment.TickCount64 - KeepMs;
        return [.. Outcomes.Values.Where(item => item.UserId == userId && item.Finished >= cutoff).OrderByDescending(item => item.Finished)];
    }
}
