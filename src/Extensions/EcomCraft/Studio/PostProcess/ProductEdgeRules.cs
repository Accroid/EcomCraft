using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ISImage = SixLabors.ImageSharp.Image;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Edge-quality checks of a product cut-out (F83), as pure logic over pixel data: a white or dark rim around the product, stray islands left
/// by the cut-out, a product cut off by the canvas, hard jagged edges, and a high share of semi-transparency. The codes become the plain-language
/// notices of the upload confirmation (F38); they never block anything, and glass or hair edges are only reported, never promised.</summary>
public static class ProductEdgeRules
{
    /// <summary>Warning code: the semi-transparent rim is much brighter than the product inside it, the classic white rim of third-party cut-outs.</summary>
    public const string FringeWhite = "fringe_white";

    /// <summary>Warning code: the semi-transparent rim is much darker than the product inside it.</summary>
    public const string FringeBlack = "fringe_black";

    /// <summary>Warning code: besides the product there are small stray islands, background the cut-out did not remove.</summary>
    public const string Islands = "islands";

    /// <summary>Warning code: the product runs into the canvas border, so part of it was cut off.</summary>
    public const string CanvasCut = "canvas_cut";

    /// <summary>Warning code: the edge has no anti-aliasing at all and many corners, so it will look jagged in the picture.</summary>
    public const string Jagged = "jagged";

    /// <summary>Warning code: so much of the image is semi-transparent (glass, hair) that the result depends on the alpha the user provided.</summary>
    public const string SemiTransparent = "semi_transparent";

    /// <summary>A pixel counts as solid (inside the product) from this alpha up, as in the rest of product compositing.</summary>
    public const byte SolidAlpha = 128;

    /// <summary>A pixel counts as transparent below this alpha, as everywhere else in product compositing.</summary>
    public const byte TransparentAlpha = 16;

    /// <summary>How much brighter (or darker) the semi-transparent rim must be than the solid product beside it, in 0–1000 luminance, to count as a fringe.</summary>
    public const int FringeLuminanceGap = 40;

    /// <summary>At least this many rim pixels beside solid product are needed before a fringe is reported, so dust alone never warns.</summary>
    public const int FringeMinPixels = 6;

    /// <summary>A visible component smaller than this many pixels is dust, not a stray island worth a notice.</summary>
    public const int IslandMinArea = 4;

    /// <summary>A visible component must be at most this fraction of the main product to count as a stray island.</summary>
    public const int IslandShareDivisor = 20;

    /// <summary>Solid pixels on the canvas border, per mille of the border ring, above which the product reads as cut off (with a floor of 8 pixels).</summary>
    public const int CanvasCutPerMille = 2;

    /// <summary>At least this share of a cut with no anti-aliasing at all must be solid before jaggedness is even measured.</summary>
    public const int JaggedMinSolid = 100;

    /// <summary>Exposed sides per boundary pixel, in percent, above which a binary edge reads as jagged: a rectangle scores about 102, a smooth blob
    /// about 110, a staircase diagonal about 137.</summary>
    public const int JaggedCornerPercent = 130;

    /// <summary>A cut with at least this share of semi-transparent pixels (per mille) is mostly glass or hair.</summary>
    public const int SemiTransparentPerMille = 80;

    /// <summary>The warning codes of a cut-out's rim, in a fixed order, or an empty list when there is nothing to complain about.</summary>
    public static List<string> Analyze(Rgba32[] pixels, int width, int height)
    {
        if (pixels is null || width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            return [];
        }
        long count = pixels.Length;
        bool[] solid = new bool[count];
        bool[] visible = new bool[count];
        long semi = 0;
        for (int i = 0; i < count; i++)
        {
            byte alpha = pixels[i].A;
            solid[i] = alpha >= SolidAlpha;
            visible[i] = alpha >= TransparentAlpha;
            if (alpha > 0 && alpha < 255)
            {
                // Any semi-transparent pixel, visible or faint dust, counts towards the glass-and-hair share.
                semi++;
            }
        }
        List<string> codes = [.. FringeCodes(pixels, width, height), .. ComponentCodes(pixels, width, height, solid, visible, semi, count)];
        if (semi * 1000 >= count * SemiTransparentPerMille)
        {
            codes.Add(SemiTransparent);
        }
        return codes;
    }

    /// <summary>The warning codes of a decoded cut-out.</summary>
    public static List<string> Analyze(Image<Rgba32> image)
    {
        Rgba32[] pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        return Analyze(pixels, image.Width, image.Height);
    }

    /// <summary>The warning codes of an encoded cut-out image, or an empty list when it cannot be decoded: the analysis never fails a request.</summary>
    public static List<string> TryAnalyze(byte[] encoded)
    {
        if (encoded is null || encoded.Length == 0)
        {
            return [];
        }
        try
        {
            using Image<Rgba32> image = ISImage.Load<Rgba32>(encoded);
            return Analyze(image);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Luminance in 0–1000, the weightings of analog television.</summary>
    private static int Luminance(Rgba32 pixel) => (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;

    /// <summary>Whether a fringe is visible: semi-transparent pixels right beside fully opaque product are compared with the product's own brightness
    /// there, so a bright product never warns about its own brightness.</summary>
    private static List<string> FringeCodes(Rgba32[] pixels, int width, int height)
    {
        long rimCount = 0, rimDiffSum = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = y * width + x;
                byte alpha = pixels[at].A;
                if (alpha == 0 || alpha >= 255)
                {
                    continue;
                }
                long besideSum = 0;
                int beside = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }
                        int ni = ny * width + nx;
                        if (pixels[ni].A >= 255)
                        {
                            besideSum += Luminance(pixels[ni]);
                            beside++;
                        }
                    }
                }
                if (beside > 0)
                {
                    rimCount++;
                    rimDiffSum += Luminance(pixels[at]) - besideSum / beside;
                }
            }
        }
        if (rimCount < FringeMinPixels)
        {
            return [];
        }
        int mean = (int)(rimDiffSum / rimCount);
        if (mean >= FringeLuminanceGap)
        {
            return [FringeWhite];
        }
        if (mean <= -FringeLuminanceGap)
        {
            return [FringeBlack];
        }
        return [];
    }

    /// <summary>Islands, a product cut off by the canvas, and jagged edges: what the shape of the solid and visible parts says about the cut.</summary>
    private static List<string> ComponentCodes(Rgba32[] pixels, int width, int height, bool[] solid, bool[] visible, long semi, long count)
    {
        List<string> codes = [];
        long largest = 0;
        long islands = 0;
        int[] part = new int[count];
        List<long> areas = [];
        Stack<int> pending = new();
        for (int start = 0; start < count; start++)
        {
            if (!visible[start] || part[start] != 0)
            {
                continue;
            }
            int id = areas.Count + 1;
            long area = 0;
            pending.Push(start);
            part[start] = id;
            while (pending.Count > 0)
            {
                int at = pending.Pop();
                area++;
                int x = at % width, y = at / width;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }
                        int ni = ny * width + nx;
                        if (visible[ni] && part[ni] == 0)
                        {
                            part[ni] = id;
                            pending.Push(ni);
                        }
                    }
                }
            }
            areas.Add(area);
            if (area > largest)
            {
                largest = area;
            }
        }
        // Stray islands: visible parts far smaller than the product itself, big enough not to be dust.
        foreach (long area in areas)
        {
            if (area >= IslandMinArea && area * IslandShareDivisor <= largest)
            {
                islands++;
            }
        }
        if (islands > 0)
        {
            codes.Add(Islands);
        }
        // A product that runs into the canvas border was cut off by it.
        long ring = 0, ringSolid = 0;
        long solidCount = 0, exposedSides = 0, boundaryPixels = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = y * width + x;
                bool onRing = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (onRing)
                {
                    ring++;
                    if (solid[at])
                    {
                        ringSolid++;
                    }
                }
                if (!solid[at])
                {
                    continue;
                }
                solidCount++;
                bool boundary = false;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }
                        int nx = x + dx, ny = y + dy;
                        bool outside = nx < 0 || ny < 0 || nx >= width || ny >= height;
                        // Only orthogonal exposure counts: a staircase step shows two exposed sides where a smooth edge shows one,
                        // and diagonal touch alone makes a pixel neither boundary nor exposed.
                        if (dx != 0 && dy != 0)
                        {
                            continue;
                        }
                        if (outside || !solid[ny * width + nx])
                        {
                            boundary = true;
                            exposedSides++;
                        }
                    }
                }
                if (boundary)
                {
                    boundaryPixels++;
                }
            }
        }
        if (ringSolid >= Math.Max(8, ring * CanvasCutPerMille / 1000))
        {
            codes.Add(CanvasCut);
        }
        // Jaggedness is only measured on a binary edge: an anti-aliased cut is smooth whatever its shape.
        if (solidCount >= JaggedMinSolid && semi * 100 <= solidCount && exposedSides * 100 >= boundaryPixels * JaggedCornerPercent)
        {
            codes.Add(Jagged);
        }
        return codes;
    }
}
