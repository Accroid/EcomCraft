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
    public void SlotsComposeTheSetInSlotKindOrder()
    {
        List<SetRules.TemplateRef> templates =
        [
            new("white-main", ["*"], ["standard"], "white_main"),
            new("scene", ["*"], ["standard"], "scene_main"),
            new("hand", ["*"], ["standard"], "model"),
            new("kindless", ["*"], ["standard"]),
            new("video", ["*"], ["video-set"], "scene_main")
        ];
        // JD-style slots: white first, then scene — the model and kindless templates stay out.
        Assert.Equal(new List<string> { "white-main", "scene" }, SetRules.SlotTemplates(templates, "jd", "standard", ["white_main", "scene_main"]).Select(template => template.Id).ToList());
        // Douyin-style: scene first and no white slot, so the white template stays out even though it suits every platform.
        Assert.Equal(new List<string> { "scene" }, SetRules.SlotTemplates(templates, "douyin", "standard", ["scene_main"]).Select(template => template.Id).ToList());
        // A ticked optional slot brings its kind in, and other sets are ignored.
        Assert.Equal(new List<string> { "scene", "white-main", "hand" }, SetRules.SlotTemplates(templates, "douyin", "standard", ["scene_main", "white_main", "model"]).Select(template => template.Id).ToList());
        // Kinds with no template in the set are skipped.
        Assert.Equal(new List<string> { "scene" }, SetRules.SlotTemplates(templates, "jd", "standard", ["scene_main", "detail"]).Select(template => template.Id).ToList());
    }

    [Fact]
    public void ReferencesExpandOneImagePerReferenceProductByProduct()
    {
        List<SetRules.Job> jobs = SetRules.ExpandReferences(2, "scene", 3);
        Assert.Equal(6, jobs.Count);
        Assert.Equal(new SetRules.Job(0, "scene", -1, 0), jobs[0]);
        Assert.Equal(new SetRules.Job(0, "scene", -1, 2), jobs[2]);
        Assert.Equal(new SetRules.Job(1, "scene", -1, 0), jobs[3]);
        Assert.Equal(new SetRules.Job(1, "scene", -1, 2), jobs[5]);
        Assert.All(jobs, job => Assert.Equal(-1, job.Shot));
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

    [Fact]
    public void StoryboardShotsExpandProductByProductAndMayRepeatTemplates()
    {
        List<SetRules.Job> jobs = SetRules.ExpandShots(2, ["scene", "scene", "detail"]);
        Assert.Equal(6, jobs.Count);
        Assert.Equal(new SetRules.Job(0, "scene", 0), jobs[0]);
        Assert.Equal(new SetRules.Job(0, "scene", 1), jobs[1]);
        Assert.Equal(new SetRules.Job(1, "detail", 2), jobs[5]);
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
