using EcomCraftStudio.Studio;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>The one-click vision model install (F69) only accepts the documented models and keeps its progress lines small and plain.</summary>
public class VisionModelInstallTests
{
    [Theory]
    [InlineData("qwen3-vl:8b-instruct", true)]
    [InlineData(" qwen3-vl:4b-instruct ", true)]
    [InlineData("qwen3-vl:4b", false)]
    [InlineData("llama3.2-vision:latest", true)]
    [InlineData("", false)]
    [InlineData("gpt-4o", false)]
    [InlineData("qwen3-vl:8b-instruct; ollama serve &", false)]
    public void AcceptsOnlyTheDocumentedModels(string model, bool expected)
    {
        Assert.Equal(expected, VisionInstallRules.IsKnownModel(model));
    }

    [Fact]
    public void ProgressLinesKeepTheLatestSegmentAndStayCapped()
    {
        Assert.Equal("pulling 42%", VisionInstallRules.CleanLine("pulling 1%\rpulling 42%"));
        Assert.Equal("", VisionInstallRules.CleanLine("  \r \n"));
        Assert.Equal(VisionInstallRules.MaxLineLength, VisionInstallRules.CleanLine(new string('a', 500)).Length);
    }

    [Fact]
    public void OllamaProgressDropsTerminalCodesAndTheBar()
    {
        string raw = "pulling ee8b93fa272a:  65% ▕████       ▏ 543 MB/839 MB  7.3 MB/s     40s\x1B[K\x1B[?25h\x1B[?2026l\x1B[?2026h\x1B[?25l\x1B[A\x1B[1Gpulling manifest \x1B[K";
        Assert.Equal("pulling ee8b93fa272a: 65% 543 MB/839 MB 7.3 MB/s 40s", VisionInstallRules.CleanLine(raw));
        Assert.Equal("pulling manifest", VisionInstallRules.CleanLine("\x1B[?25lpulling manifest \x1B[K"));
    }

    [Fact]
    public void NewerProgressOfTheSameStepReplacesTheLine()
    {
        Assert.True(VisionInstallRules.Replaces("pulling ee8b93fa272a: 10% 84 MB/839 MB", "pulling ee8b93fa272a: 65% 543 MB/839 MB"));
        Assert.True(VisionInstallRules.Replaces("pulling manifest", "pulling manifest"));
        Assert.False(VisionInstallRules.Replaces("pulling ee8b93fa272a: 100% 839 MB", "pulling 0ba8f0e314b4: 2% 1 MB/12 KB"));
        Assert.False(VisionInstallRules.Replaces("qwen3-vl:8b", "pulling manifest"));
    }
}
