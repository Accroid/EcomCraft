using EcomCraftStudio.Studio.Brand;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Brand kits (F12): per-account isolation, input checks and the prompt text a kit adds.</summary>
public class BrandKitTests
{
    [Fact]
    public void AnotherAccountsKitIsNeverFoundOrRemoved()
    {
        BrandKitIndex index = new();
        index.Add(new BrandKitRecord() { Id = "a1", UserId = "alice", Created = 1 });
        Assert.NotNull(index.Find("alice", "a1"));
        Assert.Null(index.Find("bob", "a1"));
        Assert.Null(index.Remove("bob", "a1"));
        Assert.Single(index.Items);
        Assert.Empty(index.ForUser("bob"));
    }

    [Fact]
    public void LimitsKitsPerAccount()
    {
        BrandKitIndex index = new();
        for (int i = 0; i < BrandKitIndex.MaxPerUser; i++)
        {
            Assert.Null(index.Add(new BrandKitRecord() { Id = $"k{i}", UserId = "alice" }));
        }
        Assert.NotNull(index.Add(new BrandKitRecord() { Id = "extra", UserId = "alice" }));
        Assert.Null(index.Add(new BrandKitRecord() { Id = "other", UserId = "bob" }));
    }

    [Fact]
    public void ColorsMustBeHexAndAreLimited()
    {
        Assert.Equal(new List<string> { "#0A0B0C", "#FFFFFF" }, BrandKitIndex.CleanColors(new List<string> { "#0a0b0c", " #ffffff ", "#FFFFFF" }));
        Assert.Null(BrandKitIndex.CleanColors(new List<string> { "red" }));
        Assert.Null(BrandKitIndex.CleanColors(new List<string> { "#FFF" }));
        Assert.Null(BrandKitIndex.CleanColors(Enumerable.Range(0, 6).Select(i => $"#00000{i}")));
    }

    [Theory]
    [InlineData("#000080", "navy blue")]
    [InlineData("#FF0000", "red")]
    [InlineData("#FFC0CB", "pink")]
    [InlineData("#FFFFFF", "white")]
    [InlineData("#808080", "gray")]
    [InlineData("#8B4513", "brown")]
    [InlineData("#00FF00", "green")]
    public void ColorsBecomePlainWords(string hex, string expected)
    {
        Assert.Equal(expected, BrandKitIndex.ColorName(hex));
    }

    [Fact]
    public void FragmentJoinsColorsAndBackground()
    {
        BrandKitRecord record = new() { Colors = ["#000080", "#FF0000", "#FF0001"], BackgroundPrompt = "light marble surface" };
        Assert.Equal("brand colors navy blue and red, light marble surface", BrandKitIndex.PromptFragment(record));
        Assert.Equal("", BrandKitIndex.PromptFragment(new BrandKitRecord()));
        Assert.Equal("", BrandKitIndex.PromptFragment(null));
    }

    [Fact]
    public void OnlyKnownFileNamesAreServedAndNamesAreCleaned()
    {
        Assert.Matches(BrandKitIndex.FileName, "logo.png");
        Assert.Matches(BrandKitIndex.FileName, "ref-0a1b2c3d.jpg");
        Assert.DoesNotMatch(BrandKitIndex.FileName, "../products.json");
        Assert.DoesNotMatch(BrandKitIndex.FileName, "ref-1.png");
        Assert.Equal("Acme 春季", BrandKitIndex.CleanName("  Acme\n春季 "));
        Assert.Null(BrandKitIndex.CleanName("   "));
    }
}
