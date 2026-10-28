using System.IO;
using EcomCraftStudio.Studio.Storage;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Work library location rules (R11.2, R11.4, R11.9) and sync retry backoff (R11.5).</summary>
public class StorageRulesTests
{
    /// <summary>A platform layout under the temp folder, so the tests run on Windows and Linux alike.</summary>
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ecomcraft-tests");

    /// <summary>System data folder of the test layout.</summary>
    private static readonly string Data = Path.Combine(Root, "Data");

    /// <summary>Output folder of the test layout.</summary>
    private static readonly string Output = Path.Combine(Root, "Output");

    [Fact]
    public void AcceptsAbsoluteFolderOutsideDataAndOutput()
    {
        string library = Path.Combine(Root, "Library");
        Assert.Null(StorageRules.Normalize(library + Path.DirectorySeparatorChar, Data, Output, out string normalized));
        Assert.Equal(library, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/folder")]
    public void RejectsEmptyAndRelative(string raw)
    {
        Assert.NotNull(StorageRules.Normalize(raw, Data, Output, out string normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void RejectsDataOutputAndTheirParents()
    {
        Assert.NotNull(StorageRules.Normalize(Path.Combine(Data, "EcomCraft"), Data, Output, out _));
        Assert.NotNull(StorageRules.Normalize(Output, Data, Output, out _));
        Assert.NotNull(StorageRules.Normalize(Path.Combine(Output, "local"), Data, Output, out _));
        Assert.NotNull(StorageRules.Normalize(Root, Data, Output, out _));
    }

    [Fact]
    public void SameOrInsideHandlesRootsCaseAndSeparators()
    {
        Assert.True(StorageRules.IsSameOrInside("C:/Works/a", "C:/"));
        Assert.True(StorageRules.IsSameOrInside("/mnt/nas/works", "/"));
        Assert.True(StorageRules.IsSameOrInside(@"\\NAS\Share\Works", @"\\nas\share"));
        Assert.False(StorageRules.IsSameOrInside("/mnt/nas2", "/mnt/nas"));
    }

    [Theory]
    [InlineData("raw/2026-10-28/a.png", true)]
    [InlineData("a.png", true)]
    [InlineData("../a.png", false)]
    [InlineData("raw//a.png", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:/a.png", false)]
    [InlineData("", false)]
    public void SafeRelativeStaysInsideRoot(string relative, bool expected)
    {
        Assert.Equal(expected, StorageRules.IsSafeRelative(relative));
    }

    [Fact]
    public void RetryDelayDoublesUpToTheCap()
    {
        Assert.Equal(0, StorageRules.RetryDelayMs(0));
        Assert.Equal(StorageRules.FirstRetryMs, StorageRules.RetryDelayMs(1));
        Assert.Equal(StorageRules.FirstRetryMs * 2, StorageRules.RetryDelayMs(2));
        Assert.Equal(StorageRules.MaxRetryMs, StorageRules.RetryDelayMs(30));
    }
}
