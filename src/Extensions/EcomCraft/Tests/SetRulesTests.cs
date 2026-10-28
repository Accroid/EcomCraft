using EcomCraftStudio.Studio.Generation;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>One-click sets and batches (F03): set expansion and the per-person limit (R5.7, R9.4).</summary>
public class SetRulesTests
{
    /// <summary>Templates of two sets for different platforms.</summary>
    private static List<SetRules.TemplateRef> Templates() =>
    [
        new("white-main", ["*"], ["standard"]),
        new("scene", ["*"], ["standard"]),
        new("amazon-only", ["amazon"], ["standard"]),
        new("video", ["*"], ["video-set"])
    ];

    [Fact]
    public void SetKeepsTemplatesOfTheSetThatSuitThePlatform()
    {
        Assert.Equal(new List<string> { "white-main", "scene" }, SetRules.SetTemplates(Templates(), "taobao", "standard").Select(template => template.Id).ToList());
        Assert.Equal(new List<string> { "white-main", "scene", "amazon-only" }, SetRules.SetTemplates(Templates(), "amazon", "standard").Select(template => template.Id).ToList());
        Assert.Equal(new List<string> { "white-main", "scene", "amazon-only" }, SetRules.SetTemplates(Templates(), "", "standard").Select(template => template.Id).ToList());
        Assert.Empty(SetRules.SetTemplates(Templates(), "amazon", "missing"));
    }

    [Fact]
    public void ExpandsProductByProduct()
    {
        List<SetRules.Job> jobs = SetRules.Expand(2, SetRules.SetTemplates(Templates(), "taobao", "standard"));
        Assert.Equal(4, jobs.Count);
        Assert.Equal(new SetRules.Job(0, "white-main"), jobs[0]);
        Assert.Equal(new SetRules.Job(0, "scene"), jobs[1]);
        Assert.Equal(new SetRules.Job(1, "white-main"), jobs[2]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(32, SetRules.MaxConcurrent)]
    public void ConcurrencyFollowsTheRoleWithinBounds(int roleMax, int expected)
    {
        Assert.Equal(expected, SetRules.Concurrency(roleMax));
    }
}
