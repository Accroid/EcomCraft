namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Where the product sits on the picture, relative to the canvas so it works at any output size (F02, F35).
/// <see cref="CenterX"/> and <see cref="CenterY"/> are the product center as fractions of the canvas width and height; <see cref="Size"/> is the product's
/// longer side as a fraction of the canvas's shorter side. The same placement is used to build the input canvas and to paste the product back,
/// so the two always line up. Clients only send these three numbers, checked by <see cref="TryCreate"/>.</summary>
public sealed record class Placement(double CenterX, double CenterY, double Size)
{
    /// <summary>Default placement: centered, product's longer side at 70% of the canvas's shorter side.</summary>
    public static readonly Placement Default = new(0.5, 0.5, 0.7);

    /// <summary>Smallest allowed size.</summary>
    public const double MinSize = 0.1;

    /// <summary>Largest allowed size; the product may then overflow the canvas, which is clipped.</summary>
    public const double MaxSize = 1.5;

    /// <summary>Builds a placement from client numbers, or returns false when any is missing, not finite or out of range (center 0–1, size 0.1–1.5).</summary>
    public static bool TryCreate(double? centerX, double? centerY, double? size, out Placement placement)
    {
        placement = null;
        if (centerX is not double x || centerY is not double y || size is not double s || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(s)
            || x < 0 || x > 1 || y < 0 || y > 1 || s < MinSize || s > MaxSize)
        {
            return false;
        }
        placement = new Placement(x, y, s);
        return true;
    }

    /// <summary>The product rectangle in pixels on a canvas of the given size, keeping the product's aspect ratio. Width and height are at least 1.</summary>
    public (int X, int Y, int Width, int Height) Rect(int canvasWidth, int canvasHeight, int productWidth, int productHeight)
    {
        double longest = Math.Max(1, Math.Max(productWidth, productHeight));
        double target = Size * Math.Min(canvasWidth, canvasHeight);
        int width = Math.Max(1, (int)Math.Round(productWidth / longest * target));
        int height = Math.Max(1, (int)Math.Round(productHeight / longest * target));
        int x = (int)Math.Round(CenterX * canvasWidth - width / 2.0);
        int y = (int)Math.Round(CenterY * canvasHeight - height / 2.0);
        return (x, y, width, height);
    }
}
