using EcomCraftStudio.Studio.PostProcess;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace EcomCraftStudio.Tests;

/// <summary>Product preservation (F02): placement math and pasting the original product back onto a generated picture.</summary>
public class ProductCompositeTests
{
    /// <summary>A 40x40 cut-out: a red 20x20 square with a 2x2 white "logo" in its middle, transparent around it.</summary>
    private static Image<Rgba32> Cutout()
    {
        Image<Rgba32> image = new(40, 40, new Rgba32(0, 0, 0, 0));
        for (int y = 10; y < 30; y++)
        {
            for (int x = 10; x < 30; x++)
            {
                image[x, y] = new Rgba32(200, 20, 20, 255);
            }
        }
        for (int y = 19; y < 21; y++)
        {
            for (int x = 19; x < 21; x++)
            {
                image[x, y] = new Rgba32(255, 255, 255, 255);
            }
        }
        return image;
    }

    [Fact]
    public void RectKeepsAspectAndCenters()
    {
        (int X, int Y, int Width, int Height) rect = new Placement(0.5, 0.5, 0.5).Rect(200, 100, 40, 20);
        Assert.Equal((75, 38, 50, 25), rect);
    }

    [Theory]
    [InlineData(0.5, 0.5, 0.7, true)]
    [InlineData(-0.1, 0.5, 0.7, false)]
    [InlineData(0.5, 1.2, 0.7, false)]
    [InlineData(0.5, 0.5, 0.05, false)]
    [InlineData(0.5, 0.5, 2.0, false)]
    [InlineData(0.5, 0.5, double.NaN, false)]
    public void PlacementRangeIsChecked(double x, double y, double size, bool valid)
    {
        Assert.Equal(valid, Placement.TryCreate(x, y, size, out _));
    }

    [Fact]
    public void DetectsTransparency()
    {
        using Image<Rgba32> cutout = Cutout();
        using Image<Rgba32> opaque = new(40, 40, new Rgba32(10, 10, 10, 255));
        Assert.True(ProductComposite.HasTransparency(cutout));
        Assert.False(ProductComposite.HasTransparency(opaque));
    }

    [Fact]
    public void PasteBackKeepsProductPixelsExactlyAndBackgroundUntouched()
    {
        using Image<Rgba32> cutout = Cutout();
        using Image<Rgba32> generated = new(40, 40, new Rgba32(0, 0, 255, 255));
        // Generation changed the logo area; pasting back must restore it.
        generated[20, 20] = new Rgba32(0, 255, 0, 255);
        ProductComposite.PasteBack(generated, cutout, new Placement(0.5, 0.5, 1.0));
        Assert.Equal(new Rgba32(255, 255, 255, 255), generated[20, 20]);
        Assert.Equal(new Rgba32(200, 20, 20, 255), generated[15, 15]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), generated[2, 2]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), generated[5, 20]);
    }

    [Fact]
    public void FeatherOnlySoftensInwards()
    {
        using Image<Rgba32> cutout = Cutout();
        byte[] alpha = ProductComposite.FeatheredAlpha(cutout, 2);
        Assert.Equal(0, alpha[20 * 40 + 9]);
        Assert.InRange(alpha[20 * 40 + 10], 1, 254);
        Assert.Equal(255, alpha[20 * 40 + 15]);
    }

    [Fact]
    public void CanvasPlacesProductOnWhite()
    {
        using Image<Rgba32> cutout = Cutout();
        using Image<Rgba32> canvas = ProductComposite.BuildCanvas(cutout, 80, 80, new Placement(0.5, 0.5, 0.5));
        Assert.Equal(new Rgba32(255, 255, 255, 255), canvas[2, 2]);
        Assert.Equal(new Rgba32(200, 20, 20, 255), canvas[35, 35]);
    }
}
