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
            List<ExportSlot> slots = ExportRules.ParseSlots(platform, out string error);
            Assert.True(slots is not null && slots.Count > 0, $"{platform["id"]}: {error}");
            Assert.Contains(slots, slot => slot.Kind == "white_main" || slot.Kind == "scene_main");
            Assert.All(slots, slot => Assert.InRange(slot.Preset.Width, 64, 8192));
        }
    }

    [Fact]
    public void V2ExportBecomesOneSlot()
    {
        JObject platform = JObject.Parse("""{ "id": "old", "export": { "width": 800, "height": 800, "background": "#FFFFFF", "format": "jpg", "require_white_background": true } }""");
        List<ExportSlot> slots = ExportRules.ParseSlots(platform, out string error);
        Assert.Null(error);
        ExportSlot slot = Assert.Single(slots);
        Assert.Equal("white_main", slot.Kind);
        Assert.True(slot.RequireWhite);
        Assert.False(slot.Optional);
        Assert.Equal(0, slot.MaxBytes);
        JObject scene = JObject.Parse("""{ "id": "old2", "export": { "width": 800, "height": 800 } }""");
        Assert.Equal("scene_main", Assert.Single(ExportRules.ParseSlots(scene, out _)).Kind);
    }

    [Theory]
    [InlineData("""{ "slots": [{ "id": "a", "kind": "nope", "width": 800, "height": 800 }] }""")]
    [InlineData("""{ "slots": [{ "id": "Bad Id", "kind": "scene_main", "width": 800, "height": 800 }] }""")]
    [InlineData("""{ "slots": [{ "id": "a", "kind": "scene_main", "width": 800, "height": 800, "subject_ratio_min": 2 }] }""")]
    [InlineData("""{ "slots": [{ "id": "a", "kind": "scene_main", "width": 800, "height": 800 }, { "id": "a", "kind": "detail", "width": 800, "height": 800 }] }""")]
    public void InvalidSlotsAreRejected(string json)
    {
        Assert.Null(ExportRules.ParseSlots(JObject.Parse(json), out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void SlotWithBadSizeIsSkippedAndOthersSurvive()
    {
        JObject platform = JObject.Parse("""{ "slots": [{ "id": "a", "kind": "scene_main", "width": 10, "height": 800 }, { "id": "b", "kind": "detail", "width": 800, "height": 800 }] }""");
        List<ExportSlot> slots = ExportRules.ParseSlots(platform, out _);
        Assert.Equal(["b"], slots.Select(slot => slot.Id).ToList());
    }

    [Theory]
    [InlineData(92, new[] { 92, 71, 50 })]
    [InlineData(60, new[] { 60, 55, 50 })]
    [InlineData(51, new[] { 51, 50 })]
    [InlineData(50, new[] { 50 })]
    public void QualityLadderDescendsToTheFloor(int quality, int[] expected)
    {
        Assert.Equal(expected, ExportRules.QualityLadder(quality));
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
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("   ", "product")]
    public void NamesAreSafe(string name, string expected)
    {
        Assert.Equal(expected, ExportRules.SafeName(name, "product"));
    }
}
