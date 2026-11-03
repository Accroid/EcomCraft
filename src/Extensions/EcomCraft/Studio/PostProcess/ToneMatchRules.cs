using EcomCraftStudio.Studio.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Matching a set's colour tone to one picture (F78, T101): a local colour transfer in CIE Lab (the mean and spread of each channel moved toward
/// the base picture's, Reinhard style), never a new generation. The product is protected: statistics and changes only touch pixels outside the product's
/// box (from the work's own placement metadata, widened by a margin), with a feathered edge, so the box's pixels stay byte for byte. Pictures without a
/// known product position, and pictures made without generation (white main images, real crops, laid-out text), are skipped with a reason.</summary>
public static class ToneMatchRules
{
    /// <summary>The default strength: how far the picture moves toward the base (0 keeps it, 1 matches fully).</summary>
    public const double DefaultStrength = 0.6;

    /// <summary>Most pictures one request may tone-match: the batch image cap's default.</summary>
    public const int MaxImages = 60;

    /// <summary>Width of the feathered edge around the protected product box, as a share of the picture's shorter side.</summary>
    public const double FeatherShare = 0.04;

    /// <summary>Margin added around the product box, as a share of the picture's shorter side, so the product's soft edges and shadow stay untouched too.</summary>
    public const double MarginShare = 0.03;

    /// <summary>Statistics sample every n-th pixel on each axis, which is plenty for means and spreads and keeps large pictures fast.</summary>
    private const int SampleStride = 2;

    /// <summary>Why a picture is not tone-matched, or null when it can be: "no_generated_background" for pictures made without generation (white main
    /// images, real crops, text-only screens — there is no generated scene to match), "drawn_text" for selling images and detail screens (their drawn
    /// colours are the user's choice), "no_product_position" when the work does not say where its product sits (the direct-fusion trial, older works).</summary>
    public static string SkipReason(string metadataText)
    {
        string text = metadataText ?? "";
        if (ComplianceRules.SourceKind(text) == "cutout")
        {
            return "no_generated_background";
        }
        if (text.Contains("\"ecomcraft_selling_image\"", StringComparison.Ordinal) || text.Contains("\"ecomcraft_detail_page\"", StringComparison.Ordinal))
        {
            return "drawn_text";
        }
        return ComplianceRules.ProductPlacement(text) is null ? "no_product_position" : null;
    }

    /// <summary>A strength from a request: within 0–1, the default when missing or not a number.</summary>
    public static double Strength(double? requested) => requested is double value && !double.IsNaN(value) ? Math.Clamp(value, 0, 1) : DefaultStrength;

    /// <summary>The protected product box in pixels: the placement's square widened by <see cref="MarginShare"/>, clipped to the picture.</summary>
    public static Rectangle ProductBox(Placement product, int width, int height)
    {
        float unit = Math.Min(width, height);
        float side = (float)(product.Size * unit + MarginShare * unit * 2);
        int left = (int)Math.Floor(product.CenterX * width - side / 2), top = (int)Math.Floor(product.CenterY * height - side / 2);
        int right = (int)Math.Ceiling(product.CenterX * width + side / 2), bottom = (int)Math.Ceiling(product.CenterY * height + side / 2);
        left = Math.Clamp(left, 0, width);
        top = Math.Clamp(top, 0, height);
        return new Rectangle(left, top, Math.Clamp(right, 0, width) - left, Math.Clamp(bottom, 0, height) - top);
    }

    /// <summary>How much of the change a pixel takes (0–1): nothing inside the box, rising linearly across the feather band, all of it beyond.</summary>
    public static double Weight(int x, int y, Rectangle box, double feather)
    {
        if (box.Width <= 0 || box.Height <= 0)
        {
            return 1;
        }
        double dx = Math.Max(Math.Max(box.Left - x, x - (box.Right - 1)), 0);
        double dy = Math.Max(Math.Max(box.Top - y, y - (box.Bottom - 1)), 0);
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance <= 0)
        {
            return 0;
        }
        return feather <= 0 ? 1 : Math.Min(1, distance / feather);
    }

    /// <summary>Mean and spread of the three Lab channels.</summary>
    public sealed record class LabStats(double[] Mean, double[] Spread);

    /// <summary>Lab statistics of the pixels that take the full change (outside the box and its feather), sampled; null when too few pixels remain.</summary>
    public static LabStats Stats(Image<Rgba32> image, Rectangle box, double feather)
    {
        double[] sum = new double[3], squares = new double[3];
        long count = 0;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y += SampleStride)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x += SampleStride)
                {
                    if (row[x].A < 128 || Weight(x, y, box, feather) < 1)
                    {
                        continue;
                    }
                    double[] lab = ToLab(row[x]);
                    for (int c = 0; c < 3; c++)
                    {
                        sum[c] += lab[c];
                        squares[c] += lab[c] * lab[c];
                    }
                    count++;
                }
            }
        });
        if (count < 16)
        {
            return null;
        }
        double[] mean = new double[3], spread = new double[3];
        for (int c = 0; c < 3; c++)
        {
            mean[c] = sum[c] / count;
            spread[c] = Math.Sqrt(Math.Max(0, squares[c] / count - mean[c] * mean[c]));
        }
        return new LabStats(mean, spread);
    }

    /// <summary>Moves the picture's tone toward <paramref name="target"/> (T101): each pixel outside the product box shifts in Lab by the Reinhard transfer
    /// scaled by <paramref name="strength"/> and its feather weight; pixels inside the box are not written at all.</summary>
    public static void Apply(Image<Rgba32> image, LabStats source, LabStats target, double strength, Rectangle box, double feather)
    {
        if (strength <= 0)
        {
            return;
        }
        double[] scale = new double[3];
        for (int c = 0; c < 3; c++)
        {
            // A flat channel (spread near zero) keeps its spread instead of blowing up.
            scale[c] = source.Spread[c] < 1e-3 ? 1 : Math.Clamp(target.Spread[c] / source.Spread[c], 0.33, 3);
        }
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    double weight = Weight(x, y, box, feather) * strength;
                    if (weight <= 0)
                    {
                        continue;
                    }
                    double[] lab = ToLab(row[x]);
                    double[] moved = new double[3];
                    for (int c = 0; c < 3; c++)
                    {
                        double matched = (lab[c] - source.Mean[c]) * scale[c] + target.Mean[c];
                        moved[c] = lab[c] + (matched - lab[c]) * weight;
                    }
                    Rgba32 pixel = FromLab(moved);
                    pixel.A = row[x].A;
                    row[x] = pixel;
                }
            }
        });
    }

    /// <summary>The feather band in pixels for a picture.</summary>
    public static double Feather(int width, int height) => FeatherShare * Math.Min(width, height);

    /// <summary>sRGB (8-bit) to CIE Lab (D65).</summary>
    public static double[] ToLab(Rgba32 pixel)
    {
        static double linear(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        double r = linear(pixel.R), g = linear(pixel.G), b = linear(pixel.B);
        double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
        static double f(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = f(x), fy = f(y), fz = f(z);
        return [116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }

    /// <summary>CIE Lab (D65) back to 8-bit sRGB, clamped.</summary>
    public static Rgba32 FromLab(double[] lab)
    {
        double fy = (lab[0] + 16) / 116, fx = fy + lab[1] / 500, fz = fy - lab[2] / 200;
        static double inverse(double t) => t * t * t > 0.008856 ? t * t * t : (t - 16.0 / 116) / 7.787;
        double x = inverse(fx) * 0.95047, y = inverse(fy), z = inverse(fz) * 1.08883;
        double r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        static byte encode(double c)
        {
            c = Math.Clamp(c, 0, 1);
            double v = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
            return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        }
        return new Rgba32(encode(r), encode(g), encode(b), 255);
    }
}
