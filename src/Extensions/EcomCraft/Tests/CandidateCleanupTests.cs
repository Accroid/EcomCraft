using System.IO;
using EcomCraftStudio.Studio.Storage;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Candidate cleanup rules (F39): only expired unkept candidates inside the owner's works folder with a picture or video extension are deleted.</summary>
public class CandidateCleanupTests
{
    /// <summary>A works folder under the temp directory, so the tests run on Windows and Linux alike.</summary>
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ecomcraft-tests", "candidates");

    [Fact]
    public void ExpiredNeedsPositiveRetentionAndFullDays()
    {
        long day = 86_400_000L;
        Assert.False(CleanupRules.Expired(1000, 1000 + 14 * day - 1, 14));
        Assert.True(CleanupRules.Expired(1000, 1000 + 14 * day, 14));
        Assert.False(CleanupRules.Expired(1000, 1000 + 100 * day, 0), "retention 0 disables the cleanup");
        Assert.False(CleanupRules.Expired(1000, 1000 + 100 * day, -1), "negative retention disables the cleanup");
        Assert.False(CleanupRules.Expired(0, 1000 + 100 * day, 14), "entries without a time never expire");
    }

    [Fact]
    public void OnlyWorkExtensionsInsideTheRootMayBeDeleted()
    {
        string works = Path.Combine(Root, "works");
        Assert.True(CleanupRules.SafeToDelete(Path.Combine(works, "a.png"), works));
        Assert.True(CleanupRules.SafeToDelete(Path.Combine(works, "sub", "a.MP4"), works));
        Assert.False(CleanupRules.SafeToDelete(Path.Combine(works, "notes.json"), works), "not a picture or video");
        Assert.False(CleanupRules.SafeToDelete(Path.Combine(works, "..", "outside.png"), works), "outside the root");
        Assert.False(CleanupRules.SafeToDelete(works + Path.DirectorySeparatorChar, works), "the root itself is never deleted");
        Assert.False(CleanupRules.SafeToDelete(null, works));
    }

    [Fact]
    public void DeleteWorkRemovesOnlyFilesInsideTheRoot()
    {
        string works = Path.Combine(Root, "delete-me");
        string outside = Path.Combine(Root, "outside");
        Directory.CreateDirectory(works);
        Directory.CreateDirectory(outside);
        string inside = Path.Combine(works, "old.png");
        string foreign = Path.Combine(outside, "old.png");
        File.WriteAllText(inside, "x");
        File.WriteAllText(foreign, "x");
        Assert.True(CleanupRules.DeleteWork(inside, works));
        Assert.False(File.Exists(inside));
        Assert.False(CleanupRules.DeleteWork(foreign, works), "a file outside the root is never touched");
        Assert.True(File.Exists(foreign));
        Assert.True(CleanupRules.DeleteWork(Path.Combine(works, "missing.png"), works), "a missing file counts as gone");
    }
}
