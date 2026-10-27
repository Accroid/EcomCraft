namespace EcomCraftStudio.Studio.Generation;

/// <summary>Rule 1 of the LAN platform requirements (R5): among online executors that have every node and model a request needs, pick the shortest queue,
/// then the one that finished a task longest ago. Independent of SwarmUI so the choice can be unit tested.</summary>
public static class SchedulerRules
{
    /// <summary>Error code when no executor exists or all are offline.</summary>
    public const string AllOffline = "all_offline";

    /// <summary>Error code when online executors exist but none has the required nodes and models.</summary>
    public const string MissingDependencies = "missing_dependencies";

    /// <summary>Error code when the user-selected executor is unknown, offline or lacks dependencies.</summary>
    public const string PinnedUnavailable = "pinned_unavailable";

    /// <summary>Upper bound for missing nodes or models listed per executor.</summary>
    public const int MaxProblemsPerExecutor = 3;

    /// <summary>One executor as seen by the scheduler: whether it is online and capable, what it lacks, its queue (R5.2) and when it last finished a task.</summary>
    public sealed record class Candidate(int Id, string Name, bool Online, bool Capable, IReadOnlyList<string> Problems, int Queue, long LastFinished);

    /// <summary>The scheduling result: the chosen executor ID, or an error code with a user-facing reason. A hint may accompany a successful choice.</summary>
    public sealed record class Decision(int? ChosenId, string ChosenName, string Hint, string ErrorCode, string ErrorText)
    {
        /// <summary>Whether an executor was chosen.</summary>
        public bool Success => ChosenId.HasValue;
    }

    /// <summary>Applies Rule 1. With a pinned executor the task never moves elsewhere (R5.4); a hint is added when it has a queue and another capable executor is idle.</summary>
    public static Decision Decide(IReadOnlyList<Candidate> candidates, int? pinnedId)
    {
        List<Candidate> runnable = [.. candidates.Where(candidate => candidate.Online && candidate.Capable)];
        if (pinnedId.HasValue)
        {
            Candidate pinned = candidates.FirstOrDefault(candidate => candidate.Id == pinnedId.Value);
            if (pinned is null)
            {
                return Fail(PinnedUnavailable, "所选执行服务器不存在或已被停用，请刷新后重新选择。");
            }
            if (!pinned.Online)
            {
                return Fail(PinnedUnavailable, $"所选执行服务器“{pinned.Name}”离线。请改为自动分配或选择其他服务器。");
            }
            if (!pinned.Capable)
            {
                return Fail(PinnedUnavailable, $"所选执行服务器“{pinned.Name}”无法运行此任务：{ProblemText(pinned)}。请改为自动分配或选择其他服务器。");
            }
            string hint = null;
            if (pinned.Queue > 0)
            {
                Candidate idle = runnable.FirstOrDefault(candidate => candidate.Id != pinned.Id && candidate.Queue == 0);
                if (idle is not null)
                {
                    hint = $"所选服务器“{pinned.Name}”繁忙（排队 {pinned.Queue}），而“{idle.Name}”空闲，建议取消限定。";
                }
            }
            return new Decision(pinned.Id, pinned.Name, hint, null, null);
        }
        Candidate chosen = runnable.OrderBy(candidate => candidate.Queue).ThenBy(candidate => candidate.LastFinished).ThenBy(candidate => candidate.Id).FirstOrDefault();
        if (chosen is not null)
        {
            return new Decision(chosen.Id, chosen.Name, null, null, null);
        }
        List<Candidate> online = [.. candidates.Where(candidate => candidate.Online)];
        if (online.Count == 0)
        {
            return Fail(AllOffline, "所有执行服务器都离线，任务未提交。请由管理员检查服务器状态；不会转到未登记的服务器或云端。");
        }
        IEnumerable<string> details = online.Take(5).Select(candidate => $"{candidate.Name}：{ProblemText(candidate)}");
        return Fail(MissingDependencies, $"没有执行服务器能运行此任务（{string.Join("；", details)}）。请在执行服务器上安装所需模型或节点，或选择可用模型。");
    }

    /// <summary>A failed decision.</summary>
    private static Decision Fail(string code, string text) => new(null, null, null, code, text);

    /// <summary>Up to <see cref="MaxProblemsPerExecutor"/> missing items, or a generic phrase.</summary>
    private static string ProblemText(Candidate candidate) => candidate.Problems is { Count: > 0 }
        ? string.Join("、", candidate.Problems.Take(MaxProblemsPerExecutor)) : "缺少所需节点或模型";
}
