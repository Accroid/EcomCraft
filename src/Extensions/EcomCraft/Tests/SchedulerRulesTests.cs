using EcomCraftStudio.Studio.Generation;
using Xunit;
using Candidate = EcomCraftStudio.Studio.Generation.SchedulerRules.Candidate;

namespace EcomCraftStudio.Tests;

/// <summary>Rule 1 scheduling (R5.1, R5.4, R5.5).</summary>
public class SchedulerRulesTests
{
    /// <summary>An online, capable candidate.</summary>
    private static Candidate Ready(int id, int queue, long lastFinished = 0) => new(id, $"S{id}", true, true, [], queue, lastFinished);

    [Fact]
    public void PicksShortestQueue()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 3), Ready(2, 0), Ready(3, 1)], null);
        Assert.Equal(2, decision.ChosenId);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void TieGoesToEarliestFinished()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 1, 500), Ready(2, 1, 100), Ready(3, 2, 0)], null);
        Assert.Equal(2, decision.ChosenId);
    }

    [Fact]
    public void SkipsOfflineAndIncapable()
    {
        Candidate offline = new(1, "S1", false, false, [], 0, 0);
        Candidate missing = new(2, "S2", true, false, ["模型 a"], 0, 0);
        SchedulerRules.Decision decision = SchedulerRules.Decide([offline, missing, Ready(3, 5)], null);
        Assert.Equal(3, decision.ChosenId);
    }

    [Fact]
    public void PinnedStaysAndHintsWhenAnotherIsIdle()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 2), Ready(2, 0)], 1);
        Assert.Equal(1, decision.ChosenId);
        Assert.NotNull(decision.Hint);
        Assert.Contains("S2", decision.Hint);
    }

    [Fact]
    public void PinnedWithoutQueueHasNoHint()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 0), Ready(2, 0)], 1);
        Assert.Equal(1, decision.ChosenId);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void PinnedBusyWithoutIdleAlternativeHasNoHint()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 2), Ready(2, 1)], 1);
        Assert.Equal(1, decision.ChosenId);
        Assert.Null(decision.Hint);
    }

    [Fact]
    public void PinnedOfflineFailsWithoutReassigning()
    {
        Candidate offline = new(1, "S1", false, false, [], 0, 0);
        SchedulerRules.Decision decision = SchedulerRules.Decide([offline, Ready(2, 0)], 1);
        Assert.False(decision.Success);
        Assert.Equal(SchedulerRules.PinnedUnavailable, decision.ErrorCode);
    }

    [Fact]
    public void PinnedMissingDependencyNamesTheProblem()
    {
        Candidate missing = new(1, "S1", true, false, ["节点 X"], 0, 0);
        SchedulerRules.Decision decision = SchedulerRules.Decide([missing, Ready(2, 0)], 1);
        Assert.Equal(SchedulerRules.PinnedUnavailable, decision.ErrorCode);
        Assert.Contains("节点 X", decision.ErrorText);
    }

    [Fact]
    public void PinnedUnknownFails()
    {
        SchedulerRules.Decision decision = SchedulerRules.Decide([Ready(1, 0)], 9);
        Assert.Equal(SchedulerRules.PinnedUnavailable, decision.ErrorCode);
    }

    [Fact]
    public void AllOfflineIsReported()
    {
        SchedulerRules.Decision none = SchedulerRules.Decide([], null);
        Assert.Equal(SchedulerRules.AllOffline, none.ErrorCode);
        SchedulerRules.Decision offline = SchedulerRules.Decide([new Candidate(1, "S1", false, false, [], 0, 0)], null);
        Assert.Equal(SchedulerRules.AllOffline, offline.ErrorCode);
    }

    [Fact]
    public void MissingDependenciesListsAtMostThreePerServer()
    {
        Candidate missing = new(1, "S1", true, false, ["a", "b", "c", "d"], 0, 0);
        SchedulerRules.Decision decision = SchedulerRules.Decide([missing], null);
        Assert.Equal(SchedulerRules.MissingDependencies, decision.ErrorCode);
        Assert.Contains("S1：a、b、c", decision.ErrorText);
        Assert.DoesNotContain("d", decision.ErrorText.Replace("S1：a、b、c", ""));
    }
}
