using EcomCraftStudio.Studio.Prompting;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Language-model output only ever becomes plain prompt text (F37).</summary>
public class PromptRulesTests
{
    [Fact]
    public void StripsLabelsQuotesAndMarkdown()
    {
        Assert.Equal("a glass serum bottle on stone, soft light", PromptRules.Sanitize("**Prompt:** \"a glass serum bottle on stone, soft light\""));
    }

    [Fact]
    public void KeepsOnlyTheFirstParagraphAndDropsThinking()
    {
        string output = "<think>the user wants a bottle</think>\n\nwhite sneakers on a wooden table\n\nThis prompt highlights the shoes.";
        Assert.Equal("white sneakers on a wooden table", PromptRules.Sanitize(output));
    }

    [Fact]
    public void RemovesNativeSyntaxSoItCannotChangeParameters()
    {
        string sanitized = PromptRules.Sanitize("product photo <lora:style:1> __colors__ {red|blue} <segment:face>");
        Assert.DoesNotContain("<", sanitized);
        Assert.DoesNotContain(">", sanitized);
        Assert.DoesNotContain("__", sanitized);
        Assert.DoesNotContain("{", sanitized);
        Assert.DoesNotContain("|", sanitized);
        Assert.StartsWith("product photo", sanitized);
    }

    [Fact]
    public void LimitsLength()
    {
        string longText = string.Join(", ", Enumerable.Repeat("detailed product photography", 200));
        Assert.True(PromptRules.Sanitize(longText).Length <= PromptRules.MaxPromptLength);
    }

    [Fact]
    public void EmptyOutputIsEmpty()
    {
        Assert.Equal("", PromptRules.Sanitize("   \n\n  "));
        Assert.Equal("", PromptRules.Sanitize(null));
    }

    [Fact]
    public void ComposeUsesModelTextThenStyle()
    {
        Assert.Equal("a red mug, clean studio", PromptRules.Compose("a red mug", "红色马克杯", "clean studio"));
    }

    [Fact]
    public void FallbackUsesEnglishDescriptionButNotChinese()
    {
        Assert.Equal("a red mug, clean studio", PromptRules.Compose("", "a red mug", "clean studio"));
        Assert.Equal("clean studio", PromptRules.Compose("", "红色马克杯", "clean studio"));
        Assert.Equal("", PromptRules.Compose("", "", ""));
    }

    [Fact]
    public void UserPromptsKeepEveryLineButLoseNativeSyntax()
    {
        Assert.Equal("a red mug on a table, morning light", PromptRules.CleanUserPrompt("a red mug on a table,\n\nmorning light"));
        Assert.Equal("a red mug", PromptRules.CleanUserPrompt("a red mug <lora:style:1>"));
        Assert.Equal("", PromptRules.CleanUserPrompt(null));
    }

    [Fact]
    public void PanelEnglishWinsOverChineseAndEmptyPanelFallsBack()
    {
        Assert.Equal("en", PromptRules.PanelSource("a red mug", "红色马克杯"));
        Assert.Equal("zh", PromptRules.PanelSource("", "红色马克杯"));
        Assert.Equal("zh", PromptRules.PanelSource("  ", "红色马克杯"));
        Assert.Equal("", PromptRules.PanelSource(" ", null));
    }
}
