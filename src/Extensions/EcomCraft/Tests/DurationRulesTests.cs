using EcomCraftStudio.Studio.Generation;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>The time estimates (F70) use the median of recent durations, spread a generation's wait over the images ahead and never report negative time.</summary>
public class DurationRulesTests
{
    [Fact]
    public void MedianIgnoresOneSlowRun()
    {
        Assert.Null(DurationRules.Median([]));
        Assert.Equal<double?>(10, DurationRules.Median([10]));
        Assert.Equal<double?>(11, DurationRules.Median([10, 12]));
        Assert.Equal<double?>(12, DurationRules.Median([10, 300, 12]));
    }

    [Fact]
    public void WindowKeepsTheLatestValidDurations()
    {
        DurationRules.Window window = new();
        Assert.Null(window.Typical());
        window.Add(0);
        window.Add(-5);
        window.Add(DurationRules.MaxSeconds + 1);
        window.Add(double.NaN);
        Assert.Null(window.Typical());
        for (int i = 0; i < DurationRules.WindowSize; i++)
        {
            window.Add(100);
        }
        for (int i = 0; i < DurationRules.WindowSize; i++)
        {
            window.Add(5);
        }
        Assert.Equal<double?>(5, window.Typical());
    }

    [Fact]
    public void PerImageSpreadsTheWaitOverTheImagesAhead()
    {
        Assert.Equal<double>(20, DurationRules.PerImage(80, 2, 2));
        Assert.Equal<double>(30, DurationRules.PerImage(30, 0, 1));
        Assert.Equal<double>(30, DurationRules.PerImage(30, -3, 0));
    }

    [Fact]
    public void RemainingNeverGoesNegative()
    {
        Assert.Null(DurationRules.Remaining(null, 5));
        Assert.Equal<int?>(25, DurationRules.Remaining(30, 5));
        Assert.Equal<int?>(0, DurationRules.Remaining(30, 45));
    }
}
