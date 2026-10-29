using EcomCraftStudio.Studio;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>The one-click vision model install (F69) only accepts the documented models and keeps its progress lines small and plain.</summary>
public class VisionModelInstallTests
{
    [Theory]
    [InlineData("qwen3-vl:8b", true)]
    [InlineData(" qwen3-vl:4b ", true)]
    [InlineData("llama3.2-vision:latest", true)]
    [InlineData("", false)]
    [InlineData("gpt-4o", false)]
    [InlineData("qwen3-vl:8b; ollama serve &", false)]
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
}
