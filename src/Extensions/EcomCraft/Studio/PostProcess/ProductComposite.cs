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

    /// <summary>Largest product image used for cut-out and generation, in pixels: a phone photo can pass 40 megapixels while staying under 5 MB.
    /// Bigger ones are scaled down, which results never show, since the product is resized to its placement anyway.</summary>
    public const long WorkPixels = 16_000_000;

    /// <summary>Largest product image decoded at all, in pixels; the size is checked before decoding.</summary>
    public const long MaxPixels = 100_000_000;

    /// <summary>An encoded product image scaled down to at most <paramref name="workPixels"/>: the same bytes when already small enough,
    /// re-encoded as JPEG when the source was JPEG and as PNG otherwise (so transparency stays), or null when it is over <see cref="MaxPixels"/>.
    /// Throws the ImageSharp format exceptions for unreadable data.</summary>
    public static byte[] FitForWork(byte[] bytes, long workPixels = WorkPixels)
    {
        ImageInfo info = Image.Identify(bytes);
        long pixels = (long)info.Width * info.Height;
        if (pixels > MaxPixels)
        {
            return null;
        }
        if (pixels <= workPixels)
        {
            return bytes;
        }
        double scale = Math.Sqrt((double)workPixels / pixels);
        int width = Math.Max(1, (int)(info.Width * scale));
        int height = Math.Max(1, (int)(info.Height * scale));
        using Image<Rgba32> image = Image.Load<Rgba32>(bytes);
        image.Mutate(context => context.Resize(width, height));
        using System.IO.MemoryStream stream = new();
        if (info.Metadata.DecodedImageFormat is SixLabors.ImageSharp.Formats.Jpeg.JpegFormat)
        {
            image.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder() { Quality = 92 });
        }
        else
        {
            image.SaveAsPng(stream);
        }
        return stream.ToArray();
    }

    /// <summary>Transparent margin kept around a trimmed cut-out, as a fraction of its longer side, so it still counts as a cut-out.</summary>
    public const double TrimMargin = 0.02;

    /// <summary>The cut-out cropped to the product plus a small transparent margin, so a placement's size is the product's size and not that of the
    /// photo's empty border; null when it is already tight or has no visible pixel. The page trims the same way (simple.js trimCutout).</summary>
    public static Image<Rgba32> Trim(Image<Rgba32> cutout)
    {
        int left = cutout.Width, top = cutout.Height, right = -1, bottom = -1;
        cutout.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A >= TransparentAlpha)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
        });
        if (right < 0)
        {
            return null;
        }
        int margin = Math.Max(2, (int)Math.Round(Math.Max(right - left + 1, bottom - top + 1) * TrimMargin));
        left = Math.Max(0, left - margin);
        top = Math.Max(0, top - margin);
        right = Math.Min(cutout.Width - 1, right + margin);
        bottom = Math.Min(cutout.Height - 1, bottom + margin);
        if (left == 0 && top == 0 && right == cutout.Width - 1 && bottom == cutout.Height - 1)
        {
            return null;
        }
        Rectangle crop = new(left, top, right - left + 1, bottom - top + 1);
        return cutout.Clone(context => context.Crop(crop));
    }

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

    /// <summary>The product placed on a white canvas of the output size. A <paramref name="transparent"/> canvas (the generation input, N10) keeps the white
    /// but makes it fully transparent and keeps the product's own alpha, so the workflow can load the alpha as the mask of what to generate.</summary>
    public static Image<Rgba32> BuildCanvas(Image<Rgba32> cutout, int width, int height, Placement placement, bool transparent = false)
    {
        Image<Rgba32> canvas = new(width, height, new Rgba32(255, 255, 255, transparent ? (byte)0 : (byte)255));
        (int X, int Y, int Width, int Height) rect = placement.Rect(width, height, cutout.Width, cutout.Height);
        using Image<Rgba32> product = Scaled(cutout, rect);
        Blend(canvas, product, rect.X, rect.Y, null, transparent);
        return canvas;
    }

    /// <summary>Pastes the cut-out back onto the generated picture at the same placement. Inside the product the result equals the (scaled) original pixels;
    /// the edge is softened over <paramref name="feather"/> pixels, inwards only, so no background from the original photo leaks out.</summary>
    public static void PasteBack(Image<Rgba32> generated, Image<Rgba32> cutout, Placement placement, int feather = FeatherPx)
    {
        (int X, int Y, int Width, int Height) rect = placement.Rect(generated.Width, generated.Height, cutout.Width, cutout.Height);
        using Image<Rgba32> product = Scaled(cutout, rect);
        Blend(generated, product, rect.X, rect.Y, FeatheredAlpha(product, feather), false);
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
    /// Fully opaque pixels are copied exactly; the target stays opaque, unless <paramref name="keepAlpha"/> copies the top image's alpha onto a transparent target.</summary>
    private static void Blend(Image<Rgba32> target, Image<Rgba32> top, int offsetX, int offsetY, byte[] alphaOverride, bool keepAlpha)
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
                    if (a == 255 || keepAlpha)
                    {
                        row[x] = new Rgba32(source.R, source.G, source.B, (byte)a);
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
