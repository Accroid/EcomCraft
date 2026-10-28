using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Image operations for product preservation (F02): build the input canvas from a cut-out product, and paste the original product pixels
/// back onto a generated picture so logos, text and shapes cannot be changed by generation. Only the outer edge is softened, and only inwards.</summary>
public static class ProductComposite
{
    /// <summary>Width in pixels of the softened product edge.</summary>
    public const int FeatherPx = 2;

    /// <summary>A pixel counts as transparent below this alpha.</summary>
    private const byte TransparentAlpha = 16;

    /// <summary>Whether the image is a cut-out: at least 2% of its pixels are transparent.</summary>
    public static bool HasTransparency(Image<Rgba32> image)
    {
        long transparent = 0;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                foreach (Rgba32 pixel in rows.GetRowSpan(y))
                {
                    if (pixel.A < TransparentAlpha)
                    {
                        transparent++;
                    }
                }
            }
        });
        return transparent * 50 >= (long)image.Width * image.Height;
    }

    /// <summary>The cut-out scaled to its place on a canvas of the given size; at the same size it is copied without resampling, so pixels stay exact.</summary>
    private static Image<Rgba32> Scaled(Image<Rgba32> cutout, (int X, int Y, int Width, int Height) rect) =>
        rect.Width == cutout.Width && rect.Height == cutout.Height ? cutout.Clone() : cutout.Clone(context => context.Resize(rect.Width, rect.Height));

    /// <summary>The generation input: the product placed on a plain white canvas of the output size.</summary>
    public static Image<Rgba32> BuildCanvas(Image<Rgba32> cutout, int width, int height, Placement placement)
    {
        Image<Rgba32> canvas = new(width, height, new Rgba32(255, 255, 255, 255));
        (int X, int Y, int Width, int Height) rect = placement.Rect(width, height, cutout.Width, cutout.Height);
        using Image<Rgba32> product = Scaled(cutout, rect);
        Blend(canvas, product, rect.X, rect.Y, null);
        return canvas;
    }

    /// <summary>Pastes the cut-out back onto the generated picture at the same placement. Inside the product the result equals the (scaled) original pixels;
    /// the edge is softened over <paramref name="feather"/> pixels, inwards only, so no background from the original photo leaks out.</summary>
    public static void PasteBack(Image<Rgba32> generated, Image<Rgba32> cutout, Placement placement, int feather = FeatherPx)
    {
        (int X, int Y, int Width, int Height) rect = placement.Rect(generated.Width, generated.Height, cutout.Width, cutout.Height);
        using Image<Rgba32> product = Scaled(cutout, rect);
        Blend(generated, product, rect.X, rect.Y, FeatheredAlpha(product, feather));
    }

    /// <summary>Alpha of the product with its edge softened inwards: a box blur of the alpha, never above the original alpha.</summary>
    public static byte[] FeatheredAlpha(Image<Rgba32> product, int radius)
    {
        int width = product.Width;
        int height = product.Height;
        byte[] alpha = new byte[width * height];
        product.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    alpha[y * width + x] = row[x].A;
                }
            }
        });
        if (radius <= 0)
        {
            return alpha;
        }
        // Pixels outside the product count as transparent, so the edge of the image itself also softens.
        float[] horizontal = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = x + k;
                    sum += sx < 0 || sx >= width ? 0 : alpha[y * width + sx];
                }
                horizontal[y * width + x] = sum / (2f * radius + 1);
            }
        }
        byte[] result = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = y + k;
                    sum += sy < 0 || sy >= height ? 0 : horizontal[sy * width + x];
                }
                byte blurred = (byte)Math.Clamp((int)Math.Round(sum / (2f * radius + 1)), 0, 255);
                result[y * width + x] = Math.Min(blurred, alpha[y * width + x]);
            }
        }
        return result;
    }

    /// <summary>Draws <paramref name="top"/> over <paramref name="target"/> at the offset, clipped to the target, using the given alpha (or the top image's own).
    /// Fully opaque pixels are copied exactly; the target stays opaque.</summary>
    private static void Blend(Image<Rgba32> target, Image<Rgba32> top, int offsetX, int offsetY, byte[] alphaOverride)
    {
        int topWidth = top.Width;
        int topHeight = top.Height;
        Rgba32[] topPixels = new Rgba32[topWidth * topHeight];
        top.CopyPixelDataTo(topPixels);
        target.ProcessPixelRows(rows =>
        {
            for (int ty = 0; ty < topHeight; ty++)
            {
                int y = ty + offsetY;
                if (y < 0 || y >= rows.Height)
                {
                    continue;
                }
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int tx = 0; tx < topWidth; tx++)
                {
                    int x = tx + offsetX;
                    if (x < 0 || x >= row.Length)
                    {
                        continue;
                    }
                    Rgba32 source = topPixels[ty * topWidth + tx];
                    int a = alphaOverride is null ? source.A : alphaOverride[ty * topWidth + tx];
                    if (a == 0)
                    {
                        continue;
                    }
                    if (a == 255)
                    {
                        row[x] = new Rgba32(source.R, source.G, source.B, 255);
                        continue;
                    }
                    Rgba32 below = row[x];
                    row[x] = new Rgba32((byte)((source.R * a + below.R * (255 - a) + 127) / 255), (byte)((source.G * a + below.G * (255 - a) + 127) / 255),
                        (byte)((source.B * a + below.B * (255 - a) + 127) / 255), 255);
                }
            }
        });
    }
}
