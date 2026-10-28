using EcomCraftStudio.Studio.Prompting;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Storyboards (F25): the model's answer only becomes checked shots with known templates and cleaned prompt text.</summary>
public class StoryboardRulesTests
{
    /// <summary>Templates the server allows for the platform in these tests.</summary>
    private static readonly HashSet<string> Allowed = ["scene", "detail"];

    [Fact]
    public void ReadsPointsAndShotsInTheFixedFormat()
    {
        string output = "POINT: 深层保湿\n- POINT: 天然成分\nSHOT: scene | bottle on a bathroom shelf, soft morning light | 展示使用场景\n1. SHOT: detail | close-up of the pump, water droplets | 突出质感";
        StoryboardRules.Storyboard storyboard = StoryboardRules.Parse(output, Allowed);
        Assert.Equal(new List<string> { "深层保湿", "天然成分" }, storyboard.Points);
        Assert.Equal(2, storyboard.Shots.Count);
        Assert.Equal(new StoryboardRules.Shot("scene", "bottle on a bathroom shelf, soft morning light", "展示使用场景"), storyboard.Shots[0]);
        Assert.Equal("detail", storyboard.Shots[1].TemplateId);
    }

    [Fact]
    public void DropsUnknownTemplatesEmptyFragmentsAndOtherLines()
    {
        string output = "Here is your plan:\nSHOT: secret-workflow | anything | x\nSHOT: scene |  | empty\nSHOT: scene\n{\"workflow\": 1}\nSHOT: detail | macro shot | 细节";
        StoryboardRules.Storyboard storyboard = StoryboardRules.Parse(output, Allowed);
        Assert.Single(storyboard.Shots);
        Assert.Equal("detail", storyboard.Shots[0].TemplateId);
        Assert.Empty(storyboard.Points);
    }

    [Fact]
    public void FragmentsLoseNativeSyntaxAndStayShort()
    {
        string longFragment = string.Join(", ", Enumerable.Repeat("soft light", 80));
        StoryboardRules.Storyboard storyboard = StoryboardRules.Parse($"SHOT: scene | bottle <lora:x:1> __colors__ | a\nSHOT: detail | {longFragment} | b", Allowed);
        Assert.Equal("bottle", storyboard.Shots[0].Prompt);
        Assert.True(storyboard.Shots[1].Prompt.Length <= StoryboardRules.MaxFragmentLength);
    }

    [Fact]
    public void LimitsCounts()
    {
        string output = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"POINT: point {i}\nSHOT: scene | view {i} | p"));
        StoryboardRules.Storyboard storyboard = StoryboardRules.Parse(output, Allowed);
        Assert.Equal(StoryboardRules.MaxPoints, storyboard.Points.Count);
        Assert.Equal(StoryboardRules.MaxShots, storyboard.Shots.Count);
    }

    [Fact]
    public void UserPointsAreCleanedAndDeduplicated()
    {
        Assert.Equal(new List<string> { "保湿", "无香精" }, StoryboardRules.CleanPoints(new List<string> { "  保湿 ", "", "保湿", "无<香精>" }).Select(point => point.Replace(" ", "")).ToList());
        Assert.Equal(StoryboardRules.MaxPointLength, StoryboardRules.CleanText(new string('a', 200), StoryboardRules.MaxPointLength).Length);
    }

    [Fact]
    public void FallbackIsOneShotPerSetTemplate()
    {
        List<StoryboardRules.Shot> shots = StoryboardRules.Fallback([("scene", "场景图"), ("detail", "细节图")]);
        Assert.Equal(2, shots.Count);
        Assert.Equal(new StoryboardRules.Shot("scene", "", "场景图"), shots[0]);
    }
}
