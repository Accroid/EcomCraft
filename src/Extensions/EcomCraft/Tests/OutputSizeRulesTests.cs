using EcomCraftStudio.Studio.Generation;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Output size chosen before generating: parsing, the generated size near one megapixel and the cover crop to the exact size.</summary>
public class OutputSizeRulesTests
{
    [Fact]
    public void ParsesOnlyWholeSizesInRange()
    {
        Assert.True(OutputSizeRules.TryParse(JObject.Parse("{\"width\":800,\"height\":800}"), out int width, out int height));
        Assert.Equal((800, 800), (width, height));
        Assert.False(OutputSizeRules.TryParse(JObject.Parse("{\"width\":10,\"height\":800}"), out _, out _));
        Assert.False(OutputSizeRules.TryParse(JObject.Parse("{\"width\":800.5,\"height\":800}"), out _, out _));
        Assert.False(OutputSizeRules.TryParse(new JValue("auto"), out _, out _));
    }

    [Theory]
    [InlineData(800, 800, 1024, 1024)]
    [InlineData(2000, 2000, 1024, 1024)]
    [InlineData(750, 1000, 880, 1184)]
    [InlineData(1920, 1080, 1360, 768)]
    public void GeneratesAtTheSameAspectNearOneMegapixel(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), OutputSizeRules.GenerationSize(width, height));
    }

    [Fact]
    public void CoverCropsOnlyTheRoundingDifference()
    {
        (int scaledWidth, int scaledHeight, int cropX, int cropY) = OutputSizeRules.Cover(880, 1184, 750, 1000);
        Assert.True(scaledWidth >= 750 && scaledHeight >= 1000);
        // Less than 1% of each side is cut.
        Assert.True(cropX * 2 <= 750 / 100 && cropY * 2 <= 1000 / 100);
    }
}
