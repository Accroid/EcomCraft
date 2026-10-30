using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>The output size chosen before generating: the picture is generated at the same aspect ratio near one megapixel, where the models do best, and
/// then scaled to the exact size, so a 2000 × 2000 main image is neither slow nor repeated by generating it directly. Independent of SwarmUI so it can be unit tested.</summary>
public static class OutputSizeRules
{
    /// <summary>Smallest and largest accepted output side, the same range as export sizes.</summary>
    public const int MinSide = 64, MaxSide = 8192;

    /// <summary>Pixel count pictures are generated at.</summary>
    public const long GenerationPixels = 1024 * 1024;

    /// <summary>Generated sides are multiples of this.</summary>
    public const int Step = 16;

    /// <summary>Smallest and largest generated side, the range the workflow path accepts.</summary>
    public const int MinGenerated = 256, MaxGenerated = 2048;

    /// <summary>Reads an output size { width, height } from a request; false when it is missing, not whole numbers or out of range.</summary>
    public static bool TryParse(JToken token, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (token is not JObject size || size["width"]?.Type != JTokenType.Integer || size["height"]?.Type != JTokenType.Integer)
        {
            return false;
        }
        long w = size.Value<long>("width");
        long h = size.Value<long>("height");
        if (w < MinSide || w > MaxSide || h < MinSide || h > MaxSide)
        {
            return false;
        }
        width = (int)w;
        height = (int)h;
        return true;
    }

    /// <summary>The size to generate at for an output size: the same aspect ratio at about <see cref="GenerationPixels"/>, each side a multiple of
    /// <see cref="Step"/> within <see cref="MinGenerated"/>–<see cref="MaxGenerated"/>.</summary>
    public static (int Width, int Height) GenerationSize(int width, int height)
    {
        double aspect = (double)Math.Max(1, width) / Math.Max(1, height);
        double w = Math.Sqrt(GenerationPixels * aspect);
        double h = w / aspect;
        int side(double value) => Math.Clamp((int)Math.Round(value / Step) * Step, MinGenerated, MaxGenerated);
        return (side(w), side(h));
    }

    /// <summary>How a generated picture becomes the exact output size: scaled to cover it, keeping the aspect ratio, then the centered crop.
    /// Returns the scaled size and the crop offset; the generated aspect ratio differs only by rounding, so almost nothing is cut.</summary>
    public static (int ScaledWidth, int ScaledHeight, int CropX, int CropY) Cover(int sourceWidth, int sourceHeight, int width, int height)
    {
        double scale = Math.Max((double)width / Math.Max(1, sourceWidth), (double)height / Math.Max(1, sourceHeight));
        int scaledWidth = Math.Max(width, (int)Math.Round(sourceWidth * scale));
        int scaledHeight = Math.Max(height, (int)Math.Round(sourceHeight * scale));
        return (scaledWidth, scaledHeight, (scaledWidth - width) / 2, (scaledHeight - height) / 2);
    }
}
