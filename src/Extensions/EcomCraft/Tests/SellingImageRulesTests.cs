using EcomCraftStudio.Studio.Export;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Selling-point images (F47): the user's text is checked and laid out by fixed rules, never written by AI.</summary>
public class SellingImageRulesTests
{
    [Fact]
    public void TitleIsRequiredAndLengthsAreLimited()
    {
        Assert.NotNull(SellingImageRules.Problem("", []));
        Assert.Null(SellingImageRules.Problem("春季上新", ["24 小时保湿", "无香精"]));
        Assert.NotNull(SellingImageRules.Problem(new string('字', SellingImageRules.MaxTitleLength + 1), []));
        Assert.NotNull(SellingImageRules.Problem("标题", ["一", "二", "三", "四"]));
        Assert.NotNull(SellingImageRules.Problem("标题", [new string('字', SellingImageRules.MaxPointLength + 1)]));
    }

    [Fact]
    public void EmojiIsRejectedButCommonSignsStay()
    {
        Assert.Equal("😀", SellingImageRules.UnsupportedCharacter("好看😀"));
        Assert.NotNull(SellingImageRules.Problem("新品☀", []));
        Assert.Null(SellingImageRules.UnsupportedCharacter("耐热 120℃ ★ 2×更持久 Brand™"));
        Assert.Null(SellingImageRules.Problem("Spring Sale 春季", ["Free shipping", "买二送一"]));
    }

    [Fact]
    public void CleanTextKeepsOneLine()
    {
        Assert.Equal("a b", SellingImageRules.CleanText("  a\n\t b ​"));
        Assert.Equal(2, SellingImageRules.Length("😀a"));
    }

    [Fact]
    public void AccentFallsBackAndTextColorFollowsContrast()
    {
        Assert.Equal(SellingImageRules.DefaultAccent, SellingImageRules.Accent("red"));
        Assert.Equal("#ABCDEF", SellingImageRules.Accent("#abcdef"));
        Assert.Equal("#FFFFFF", SellingImageRules.TextOn("#1F2937"));
        Assert.Equal("#111111", SellingImageRules.TextOn("#FFF1C1"));
    }

    [Theory]
    [InlineData("banner", 1000, 1000)]
    [InlineData("side", 1600, 900)]
    [InlineData("badges", 800, 1200)]
    public void LayoutsStayInsideThePicture(string layout, int width, int height)
    {
        SellingImageRules.Plan plan = SellingImageRules.Layout(layout, width, height);
        foreach (SellingImageRules.Box box in new[] { plan.Panel, plan.Title, plan.Points })
        {
            Assert.True(box.X >= 0 && box.Y >= 0 && box.Width > 0 && box.Height > 0);
            Assert.True(box.X + box.Width <= width + 0.5f && box.Y + box.Height <= height + 0.5f);
        }
        Assert.True(plan.TitleSize > plan.PointSize);
    }

    [Fact]
    public void FitSizeShrinksUntilTheTextFits()
    {
        // A fake measure: 10 characters of width 0.6 × size.
        float size = SellingImageRules.FitSize(fontSize => 10 * 0.6f * fontSize, 300, 100, 10);
        Assert.True(size <= 50 && size > 45);
        Assert.Equal(10f, SellingImageRules.FitSize(fontSize => 1e6f, 300, 100, 10));
    }
}
