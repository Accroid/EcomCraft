using EcomCraftStudio.Studio.Projects;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Projects (F04): per-account isolation, limits, deletion and reading recipes from work metadata.</summary>
public class ProjectIndexTests
{
    [Fact]
    public void AnotherAccountsProjectIsNeverFoundOrRemoved()
    {
        ProjectIndex index = new();
        index.Add(new ProjectRecord() { Id = "p1", UserId = "alice", Name = "客户 A" });
        Assert.NotNull(index.Find("alice", "p1"));
        Assert.Null(index.Find("bob", "p1"));
        Assert.Null(index.Remove("bob", "p1"));
        Assert.Empty(index.ForUser("bob"));
        Assert.NotNull(index.Remove("alice", "p1"));
        Assert.Empty(index.Items);
    }

    [Fact]
    public void LimitsProjectsPerAccount()
    {
        ProjectIndex index = new();
        for (int i = 0; i < ProjectIndex.MaxPerUser; i++)
        {
            Assert.Null(index.Add(new ProjectRecord() { Id = $"p{i}", UserId = "alice" }));
        }
        Assert.NotNull(index.Add(new ProjectRecord() { Id = "extra", UserId = "alice" }));
        Assert.Null(index.Add(new ProjectRecord() { Id = "other", UserId = "bob" }));
    }

    [Fact]
    public void WorksAreFiledOnceAndCanBeRemoved()
    {
        ProjectRecord project = new();
        Assert.Equal(2, ProjectIndex.AddWorks(project, [new ProjectWork() { Path = "a.png" }, new ProjectWork() { Path = "b.png" }, new ProjectWork() { Path = "a.png" }]));
        Assert.Equal(0, ProjectIndex.AddWorks(project, [new ProjectWork() { Path = "a.png" }]));
        Assert.Equal(1, ProjectIndex.RemoveWorks(project, ["a.png", "missing.png"]));
        Assert.Single(project.Works);
    }

    [Fact]
    public void TooManyWorksAreRefused()
    {
        ProjectRecord project = new();
        Assert.Equal(-1, ProjectIndex.AddWorks(project, Enumerable.Range(0, ProjectIndex.MaxWorks + 1).Select(i => new ProjectWork() { Path = $"{i}.png" })));
        Assert.Empty(project.Works);
    }

    [Fact]
    public void RecipeComesFromMetadataWithoutTheTemplatesOwnText()
    {
        string metadata = """
            { "sui_image_params": { "prompt": "product photo, a red mug on oak, brand color red, studio light", "seed": 12345, "width": 1024, "height": 1024 },
              "sui_extra_data": { "ecomcraft_template": "scene", "ecomcraft_execution_server_name": "GPU-1" } }
            """;
        ProjectRecipe recipe = ProjectIndex.ParseMetadata(metadata, template => template == "scene" ? ("product photo,", "studio light") : ("", ""));
        Assert.Equal("scene", recipe.Template);
        Assert.Equal(12345L, recipe.Seed);
        Assert.Equal("a red mug on oak, brand color red,", recipe.Prompt);
        Assert.Equal("GPU-1", recipe.Server);
        Assert.Equal(1024, recipe.Width);
    }

    [Fact]
    public void UnreadableOrForeignMetadataGivesAnEmptyRecipe()
    {
        Assert.Null(ProjectIndex.ParseMetadata("not json", null).Template);
        Assert.Null(ProjectIndex.ParseMetadata("""{ "sui_extra_data": { "ecomcraft_template": "../evil" } }""", null).Template);
        Assert.Equal("", ProjectIndex.ParseMetadata("", null).Prompt);
    }

    [Fact]
    public void NamesAreCleaned()
    {
        Assert.Equal("客户 A", ProjectIndex.CleanText("  客户\nA ", ProjectIndex.MaxNameLength));
        Assert.Null(ProjectIndex.CleanText(" \t ", ProjectIndex.MaxNameLength));
        Assert.Equal(ProjectIndex.MaxNameLength, ProjectIndex.CleanText(new string('x', 99), ProjectIndex.MaxNameLength).Length);
    }
}
