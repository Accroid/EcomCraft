using EcomCraftStudio.Studio.Storage;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Disk-space thresholds (F46): the warning and critical levels with disabled and unknown cases.</summary>
public class DiskRulesTests
{
    private static readonly long GB = 1024L * 1024 * 1024;

    [Fact]
    public void LevelsFollowTheThresholds()
    {
        Assert.Equal("ok", DiskRules.Level(50 * GB, DiskRules.Bytes(10), DiskRules.Bytes(2)));
        Assert.Equal("low", DiskRules.Level(10 * GB, DiskRules.Bytes(10), DiskRules.Bytes(2)));
        Assert.Equal("critical", DiskRules.Level(2 * GB, DiskRules.Bytes(10), DiskRules.Bytes(2)));
        Assert.Equal("critical", DiskRules.Level(0, DiskRules.Bytes(10), DiskRules.Bytes(2)));
    }

    [Fact]
    public void DisabledThresholdsAndUnknownSpaceNeverTrigger()
    {
        Assert.Equal("ok", DiskRules.Level(1 * GB, 0, 0));
        Assert.Equal("low", DiskRules.Level(1 * GB, DiskRules.Bytes(10), 0));
        Assert.Equal("ok", DiskRules.Level(null, DiskRules.Bytes(10), DiskRules.Bytes(2)));
    }

    [Fact]
    public void BytesConvertsGbAndDisablesOnZeroOrLess()
    {
        Assert.Equal(0, DiskRules.Bytes(0));
        Assert.Equal(0, DiskRules.Bytes(-3));
        Assert.Equal(3 * GB, DiskRules.Bytes(3));
    }
}
