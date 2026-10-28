using EcomCraftStudio.Studio.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Compliance (F22): the pure-white border check and the AI provenance metadata.</summary>
public class ComplianceTests
{
    [Fact]
    public void PureWhiteBorderPasses()
    {
        using Image<Rgb24> image = new(100, 100, new Rgb24(255, 255, 255));
        image[50, 50] = new Rgb24(10, 10, 10);
        Assert.Equal(0, ComplianceRules.NonWhiteBorderFraction(image));
    }

    [Fact]
    public void OffWhiteBorderIsDetected()
    {
        using Image<Rgb24> image = new(100, 100, new Rgb24(255, 255, 255));
        image[0, 0] = new Rgb24(254, 255, 255);
        Assert.True(ComplianceRules.NonWhiteBorderFraction(image) > 0);
        using Image<Rgb24> grey = new(100, 100, new Rgb24(250, 250, 250));
        Assert.Equal(1, ComplianceRules.NonWhiteBorderFraction(grey));
    }

    [Theory]
    [InlineData("{\"sui_extra_data\":{\"ecomcraft_cutout_only\":\"true\"}}", "cutout", null)]
    [InlineData("{\"sui_extra_data\":{\"ecomcraft_preserve\":\"true\"}}", "composite", ComplianceRules.CompositeWithTrainedAlgorithmicMedia)]
    [InlineData("{\"sui_image_params\":{\"prompt\":\"x\"}}", "generated", ComplianceRules.TrainedAlgorithmicMedia)]
    [InlineData("", "generated", ComplianceRules.TrainedAlgorithmicMedia)]
    public void SourceKindDecidesTheMarking(string metadata, string kind, string type)
    {
        Assert.Equal(kind, ComplianceRules.SourceKind(metadata));
        Assert.Equal(type, ComplianceRules.DigitalSourceType(kind));
    }

    [Fact]
    public void XmpCarriesTheDigitalSourceTypeAndEscapesText()
    {
        string xmp = ComplianceRules.Xmp(ComplianceRules.TrainedAlgorithmicMedia, "AI <generated> & co");
        Assert.Contains("Iptc4xmpExt:DigitalSourceType=\"http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia\"", xmp);
        Assert.Contains("AI &lt;generated&gt; &amp; co", xmp);
        Assert.Contains("xmp:CreatorTool=\"EcomCraft Studio\"", xmp);
    }
}
