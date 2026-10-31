using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Image operations for product preservation (F02): build the input canvas from a cut-out product, and paste the original product pixels
/// back onto a generated picture so logos, text and shapes cannot be changed by generation. Only the outer edge is softened, and only inwards.
/// The standard workflows use <see cref="PasteBlended"/>, which also takes light and color from the generated picture.</summary>
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
    public static Image<Rgba32> BuildCanvas(Image<Rgba32> cutout, int width, int height, Placement placement, bool transparent = false, byte fill = 255)
    {
        Image<Rgba32> canvas = new(width, height, new Rgba32(fill, fill, fill, transparent ? (byte)0 : (byte)255));
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

    /// <summary>Canvas fill for workflows that repaint around the product: neutral gray leaves no bright halo at the product's edge, unlike white.</summary>
    public const byte NeutralFill = 128;

    /// <summary>Softened product edge of the blended paste, in pixels.</summary>
    private const int BlendFeatherPx = 3;

    /// <summary>Window of the tone transfer as a fraction of the product's longer side: wider than text strokes, so only light and color come from the generated picture.</summary>
    private const double ToneRadius = 0.03;

    /// <summary>How much of the generated light and color reaches the product.</summary>
    private const double ToneStrength = 0.8;

    /// <summary>Largest change of a color channel, so brand colors stay recognizable whatever the generation drew there.</summary>
    private const int MaxToneShift = 40;

    /// <summary>Pastes the cut-out back so it belongs to the scene: details (logo, text, shape) come from the original pixels, while light and color come from
    /// the generated picture (frequency separation), limited to <see cref="MaxToneShift"/>; a soft contact shadow is drawn under the product.</summary>
    public static void PasteBlended(Image<Rgba32> generated, Image<Rgba32> cutout, Placement placement)
    {
        (int X, int Y, int Width, int Height) rect = placement.Rect(generated.Width, generated.Height, cutout.Width, cutout.Height);
        using Image<Rgba32> product = Scaled(cutout, rect);
        byte[] alpha = FeatheredAlpha(product, BlendFeatherPx);
        TransferTone(product, generated, rect.X, rect.Y, alpha);
        DrawContactShadow(generated, product, rect.X, rect.Y);
        Blend(generated, product, rect.X, rect.Y, alpha, false);
    }

    /// <summary>Adds to each product pixel the blurred difference between the generated picture and the product, measured only where the product is fully opaque
    /// and on the canvas, so neither the background nor the fine details of either image take part.</summary>
    private static void TransferTone(Image<Rgba32> product, Image<Rgba32> generated, int offsetX, int offsetY, byte[] alpha)
    {
        int width = product.Width, height = product.Height;
        Rgba32[] pixels = new Rgba32[width * height];
        product.CopyPixelDataTo(pixels);
        Rgba32[] below = new Rgba32[generated.Width * generated.Height];
        generated.CopyPixelDataTo(below);
        int radius = Math.Max(4, (int)Math.Round(Math.Max(width, height) * ToneRadius));
        int stride = width + 1;
        // Summed-area tables keep the blur independent of the radius.
        long[] weights = new long[stride * (height + 1)];
        long[][] sums = [new long[weights.Length], new long[weights.Length], new long[weights.Length]];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int gx = x + offsetX, gy = y + offsetY;
                bool used = alpha[y * width + x] == 255 && gx >= 0 && gy >= 0 && gx < generated.Width && gy < generated.Height;
                Rgba32 from = pixels[y * width + x];
                Rgba32 to = used ? below[gy * generated.Width + gx] : from;
                int at = (y + 1) * stride + x + 1;
                weights[at] = (used ? 1 : 0) + weights[at - 1] + weights[at - stride] - weights[at - stride - 1];
                sums[0][at] = to.R - from.R + sums[0][at - 1] + sums[0][at - stride] - sums[0][at - stride - 1];
                sums[1][at] = to.G - from.G + sums[1][at - 1] + sums[1][at - stride] - sums[1][at - stride - 1];
                sums[2][at] = to.B - from.B + sums[2][at - 1] + sums[2][at - stride] - sums[2][at - stride - 1];
            }
        }
        byte Shifted(byte value, long[] sum, int a, int b, int c, int d, long count)
        {
            double shift = (sum[a] - sum[b] - sum[c] + sum[d]) / (double)count * ToneStrength;
            return (byte)Math.Clamp((int)Math.Round(value + Math.Clamp(shift, -MaxToneShift, MaxToneShift)), 0, 255);
        }
        for (int y = 0; y < height; y++)
        {
            int top = Math.Max(0, y - radius), bottom = Math.Min(height, y + radius + 1);
            for (int x = 0; x < width; x++)
            {
                int left = Math.Max(0, x - radius), right = Math.Min(width, x + radius + 1);
                int a = bottom * stride + right, b = top * stride + right, c = bottom * stride + left, d = top * stride + left;
                long count = weights[a] - weights[b] - weights[c] + weights[d];
                if (count > 0)
                {
                    Rgba32 pixel = pixels[y * width + x];
                    product[x, y] = new Rgba32(Shifted(pixel.R, sums[0], a, b, c, d, count), Shifted(pixel.G, sums[1], a, b, c, d, count), Shifted(pixel.B, sums[2], a, b, c, d, count), pixel.A);
                }
            }
        }
    }

    /// <summary>Darkens the scene under the product's base: a tight dark core and a wider soft shadow. The base is the lowest rows of the product's opaque pixels.</summary>
    private static void DrawContactShadow(Image<Rgba32> generated, Image<Rgba32> product, int offsetX, int offsetY)
    {
        int bottom = -1;
        for (int y = product.Height - 1; y >= 0 && bottom < 0; y--)
        {
            for (int x = 0; x < product.Width && bottom < 0; x++)
            {
                if (product[x, y].A >= 128)
                {
                    bottom = y;
                }
            }
        }
        if (bottom < 0)
        {
            return;
        }
        int left = product.Width, right = -1;
        for (int y = Math.Max(0, bottom - Math.Max(2, product.Height / 20)); y <= bottom; y++)
        {
            for (int x = 0; x < product.Width; x++)
            {
                if (product[x, y].A >= 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }
        double centerX = offsetX + (left + right) / 2.0, centerY = offsetY + bottom, radius = Math.Max(2, (right - left + 1) / 2.0);
        DarkenEllipse(generated, centerX, centerY, radius * 1.45, radius * 0.28, 0.25);
        DarkenEllipse(generated, centerX, centerY, radius * 1.08, radius * 0.1, 0.55);
    }

    /// <summary>Multiplies the picture by a soft ellipse: <paramref name="strength"/> at its center, fading to nothing at its edge.</summary>
    private static void DarkenEllipse(Image<Rgba32> image, double centerX, double centerY, double radiusX, double radiusY, double strength)
    {
        image.ProcessPixelRows(rows =>
        {
            for (int y = Math.Max(0, (int)(centerY - radiusY)); y < Math.Min(rows.Height, (int)(centerY + radiusY) + 1); y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = Math.Max(0, (int)(centerX - radiusX)); x < Math.Min(row.Length, (int)(centerX + radiusX) + 1); x++)
                {
                    double dx = (x - centerX) / radiusX, dy = (y - centerY) / radiusY, distance = dx * dx + dy * dy;
                    if (distance < 1)
                    {
                        double keep = 1 - strength * (1 - distance) * (1 - distance);
                        Rgba32 pixel = row[x];
                        row[x] = new Rgba32((byte)(pixel.R * keep), (byte)(pixel.G * keep), (byte)(pixel.B * keep), pixel.A);
                    }
                }
            }
        });
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
