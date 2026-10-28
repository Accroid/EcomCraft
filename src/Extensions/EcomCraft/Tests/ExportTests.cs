using System.IO;
using EcomCraftStudio.Studio.Export;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Platform export (F05): presets, fitting, padding colors and safe names.</summary>
public class ExportTests
{
    [Fact]
    public void BuiltinPresetsAllParse()
    {
        JArray platforms = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "platforms.json")))["platforms"];
        Assert.True(platforms.Count >= 9);
        foreach (JObject platform in platforms.OfType<JObject>())
        {
            ExportPreset preset = ExportRules.Parse(platform["export"] as JObject, out string error);
            Assert.True(preset is not null, $"{platform["id"]}: {error}");
            Assert.False(string.IsNullOrWhiteSpace(preset.Source));
        }
    }

    [Fact]
    public void AmazonPresetIsPureWhite()
    {
        ExportPreset preset = ExportRules.Parse(JObject.Parse("""{ "width": 2000, "height": 2000, "background": "#FFFFFF", "format": "jpg" }"""), out _);
        Assert.Equal((255, 255, 255), (preset.R, preset.G, preset.B));
    }

    [Theory]
    [InlineData("""{ "width": 10, "height": 800 }""")]
    [InlineData("""{ "width": 800, "height": 800, "background": "white" }""")]
    [InlineData("""{ "width": 800, "height": 800, "format": "gif" }""")]
    [InlineData("""{ "width": 800, "height": 800, "quality": 10 }""")]
    public void InvalidPresetsAreRejected(string json)
    {
        Assert.Null(ExportRules.Parse(JObject.Parse(json), out string error));
        Assert.NotNull(error);
    }

    [Fact]
    public void FitKeepsAspectAndCenters()
    {
        Assert.Equal((0, 200, 800, 400), ExportRules.Fit(2000, 1000, 800, 800));
        Assert.Equal((200, 0, 400, 800), ExportRules.Fit(500, 1000, 800, 800));
        Assert.Equal((0, 0, 800, 800), ExportRules.Fit(100, 100, 800, 800));
    }

    [Fact]
    public void RenderPadsWithTheBackgroundAndShowsItThroughTransparency()
    {
        ExportPreset preset = new(100, 100, 255, 255, 255, "jpg", 92, false, "test");
        using Image<Rgba32> source = new(100, 50, new Rgba32(10, 20, 30, 255));
        source[0, 0] = new Rgba32(0, 0, 0, 0);
        using Image<Rgb24> rendered = ExportImage.Render(source, preset);
        Assert.Equal(new Rgb24(255, 255, 255), rendered[50, 5]);
        Assert.Equal(new Rgb24(255, 255, 255), rendered[50, 95]);
        Assert.Equal(new Rgb24(10, 20, 30), rendered[50, 50]);
        Assert.Equal(new Rgb24(255, 255, 255), rendered[0, 25]);
    }

    [Theory]
    [InlineData("红色 马克杯", "红色 马克杯")]
    [InlineData("../../etc", "_.._etc")]
    [InlineData("a/b\c:d", "a_b_c_d")]
    [InlineData("   ", "product")]
    public void NamesAreSafe(string name, string expected)
    {
        Assert.Equal(expected, ExportRules.SafeName(name, "product"));
    }
}
