using System.IO;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>How a request-embedded image reaches its workflow at the output size (T98). The published path used to stretch every embedded image to the
/// generation size — the old upload behaviour — which distorted the edit trial's product angles and flattened its reference. Pure size math here so it can
/// be unit tested; the ImageSharp step reads and writes data URLs.</summary>
public static class EmbedResizeRules
{
    /// <summary>The centered crop of a picture at the output's aspect ratio: the largest rectangle of the source that matches the ratio, centered. The
    /// crop (never a stretch) keeps the picture's proportions; the caller scales the crop to the exact size afterwards.</summary>
    public static (int X, int Y, int Width, int Height) CenterCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
        {
            return (0, 0, Math.Max(1, sourceWidth), Math.Max(1, sourceHeight));
        }
        double aspect = (double)targetWidth / targetHeight;
        int width, height;
        if ((double)sourceWidth / sourceHeight > aspect)
        {
            height = sourceHeight;
            width = Math.Max(1, (int)Math.Round(sourceHeight * aspect));
        }
        else
        {
            width = sourceWidth;
            height = Math.Max(1, (int)Math.Round(sourceWidth / aspect));
        }
        return ((sourceWidth - width) / 2, (sourceHeight - height) / 2, width, height);
    }

    /// <summary>One data URL center-cropped to the output's aspect and scaled to the exact output size (T98): the reference's proportions survive the trip
    /// into the workflow. Null when the bytes cannot be read as a picture.</summary>
    public static string CropToSize(string dataUrl, int width, int height)
    {
        int comma = dataUrl?.IndexOf(',') ?? -1;
        if (comma < 0)
        {
            return null;
        }
        try
        {
            using Image<Rgba32> image = ISImage.Load<Rgba32>(Convert.FromBase64String(dataUrl[(comma + 1)..]));
            (int x, int y, int cropWidth, int cropHeight) = CenterCrop(image.Width, image.Height, width, height);
            image.Mutate(context => context.Crop(new Rectangle(x, y, cropWidth, cropHeight)).Resize(width, height));
            using MemoryStream stream = new();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is FormatException or SixLabors.ImageSharp.UnknownImageFormatException or SixLabors.ImageSharp.InvalidImageContentException
            or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
