using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>The output size chosen before generating: the picture is generated at the same aspect ratio near one megapixel, where the models do best, and
/// then scaled to the exact size, so a 2000 × 2000 main image is neither slow nor repeated by generating it directly. Independent of SwarmUI so it can be unit tested.</summary>
public static class OutputSizeRules
{
    /// <summary>Smallest accepted output side.</summary>
    public const int MinSide = 64;

    /// <summary>Where the largest accepted output side comes from: the current recipe's cap, set at startup (T118); tests keep the pre-recipe default.</summary>
    internal static Func<int> MaxEdgeSource = () => 2048;

    /// <summary>Largest accepted output side: the current recipe's longest-edge cap, never above 2048 (A24: today's 2K; a recipe may lower it).
    /// Export presets accept the same range, so a platform slot above it is refused. The number also appears in the request error messages.</summary>
    public static int MaxSide => (int)Math.Clamp(MaxEdgeSource(), MinSide, 2048);

    /// <summary>Pixel count pictures are generated at.</summary>
    public const long GenerationPixels = 1024 * 1024;

    /// <summary>Generated sides are multiples of the current recipe's size step, read per call like the edge cap (M1: the official Turbo
    /// template prefers multiples of 32); tests keep the pre-recipe default.</summary>
    internal static Func<int> StepSource = () => 16;

    /// <summary>Generated sides are multiples of this: the current recipe's size step, bounded like the recipe parser bounds its own values.</summary>
    public static int Step => Math.Clamp(StepSource(), 8, 128);

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

    /// <summary>The plan for matching one picture to a platform slot (T107): the outpaint amounts that bring the picture to the slot's aspect ratio with the
    /// subject centered, then the upscale factor towards its size. Below 1.5 the model is not used and the export scales instead; above 4 the plan covers the
    /// first pass and <paramref name="RemainingFactor"/> says the rest. No step leaves the recipe's longest-edge cap (A24): what stops there is a note code.</summary>
    /// <param name="Notes">Why the plan stops short: "outpaint_capped" (the canvas reached the cap before the slot's ratio), "upscale_capped" (the factor was
    /// lowered to keep the result under the cap) and "two_pass" (the slot needs more than one 4x pass).</param>
    public sealed record class SlotFitPlan(int SourceWidth, int SourceHeight, int TargetWidth, int TargetHeight, int Step, int MaxEdge,
        int CanvasWidth, int CanvasHeight, int Left, int Top, int Right, int Bottom,
        double NeededFactor, double UpscaleFactor, double RemainingFactor, int UpscaleWidth, int UpscaleHeight, string[] Notes)
    {
        /// <summary>Whether the plan outpaints at all; ratios that differ only by rounding do not.</summary>
        public bool Outpaints => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;
    }

    /// <summary>Plans how a picture reaches a platform slot (T107): symmetric outpaint on the one axis the ratios differ (the subject stays centered), the
    /// extended side a multiple of <paramref name="step"/>, then an upscale factor inside 1.5–4 towards the slot's size. Both steps stay under
    /// <paramref name="maxEdge"/> (A24): the canvas and the upscaled result stop at its step-rounded value and the plan says so in its notes. Pure calculation
    /// for the page to word; the request itself still goes through the existing outpaint and upscale validation.</summary>
    public static SlotFitPlan PlanSlotFit(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, int step, int maxEdge)
    {
        step = Math.Clamp(step, 8, 128);
        int sourceSafeWidth = Math.Max(1, sourceWidth);
        int sourceSafeHeight = Math.Max(1, sourceHeight);
        // The cap every step must respect, aligned to the step the way generation sizes are.
        int capSide = Math.Max(step, Math.Clamp(maxEdge, MinSide, 2048) / step * step);
        List<string> notes = [];
        int left = 0, top = 0, right = 0, bottom = 0;
        int canvasWidth = sourceSafeWidth, canvasHeight = sourceSafeHeight;
        int roundToStep(double value) => (int)Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        double targetRatio = (double)Math.Max(1, targetWidth) / Math.Max(1, targetHeight);
        if (targetRatio * canvasHeight > canvasWidth)
        {
            // The slot is relatively wider: extend the canvas sideways so the ratios meet, the subject staying centered.
            int extended = roundToStep(canvasHeight * targetRatio);
            if (extended > capSide)
            {
                extended = capSide;
                notes.Add("outpaint_capped");
            }
            if (extended > canvasWidth)
            {
                left = (extended - canvasWidth) / 2;
                right = extended - canvasWidth - left;
                canvasWidth = extended;
            }
        }
        else if (targetRatio * canvasHeight < canvasWidth)
        {
            // The slot is relatively taller: extend the canvas up and down the same way.
            int extended = roundToStep(canvasWidth / targetRatio);
            if (extended > capSide)
            {
                extended = capSide;
                notes.Add("outpaint_capped");
            }
            if (extended > canvasHeight)
            {
                top = (extended - canvasHeight) / 2;
                bottom = extended - canvasHeight - top;
                canvasHeight = extended;
            }
        }
        // The factor the slot needs from the canvas: the export fits the picture entirely, so the smaller side ratio decides.
        double needed = Math.Min((double)Math.Max(1, targetWidth) / canvasWidth, (double)Math.Max(1, targetHeight) / canvasHeight);
        double factor = 0;
        double remaining = 0;
        if (needed >= 1.5)
        {
            // Quarter steps like the page's slider, rounded up so the pass covers the slot instead of falling just short of it.
            factor = Math.Min(Math.Ceiling(needed * 4) / 4, 4);
            if (needed > 4)
            {
                remaining = Math.Round(needed / 4, 2);
                notes.Add("two_pass");
            }
            // The pass may not push the long side past the cap (A24); the export scales the rest of the way.
            double capped = (double)capSide / Math.Max(canvasWidth, canvasHeight);
            if (factor > capped)
            {
                factor = Math.Floor(capped * 4) / 4;
                notes.Add("upscale_capped");
            }
            if (factor < 1.5)
            {
                factor = 0;
            }
        }
        int upscaleWidth = canvasWidth, upscaleHeight = canvasHeight;
        if (factor >= 1.5)
        {
            upscaleWidth = Math.Min(capSide, (int)Math.Round(canvasWidth * factor, MidpointRounding.AwayFromZero));
            upscaleHeight = Math.Min(capSide, (int)Math.Round(canvasHeight * factor, MidpointRounding.AwayFromZero));
        }
        return new SlotFitPlan(sourceSafeWidth, sourceSafeHeight, Math.Max(1, targetWidth), Math.Max(1, targetHeight), step, maxEdge,
            canvasWidth, canvasHeight, left, top, right, bottom, Math.Round(needed, 2), Math.Round(factor, 2), remaining, upscaleWidth, upscaleHeight, [.. notes]);
    }
}
