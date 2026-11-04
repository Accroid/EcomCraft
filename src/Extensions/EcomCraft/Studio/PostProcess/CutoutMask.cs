using System.IO;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Turns the execution server's background mask into the user's own photo with an alpha channel (F35). Pure image logic, independent of
/// SwarmUI, so the encoding can be unit tested.</summary>
public static class CutoutMask
{
    /// <summary>Puts the mask into the alpha of the user's own photo: white in the mask image is background (SwarmRemBg returns 1 − alpha), so alpha = 255 − mask.
    /// The PNG is always written with an alpha channel: the encoder otherwise keeps the source PNG's colour type, and an opaque RGB photo would
    /// come back as the same opaque picture.</summary>
    public static byte[] Combine(byte[] photo, byte[] maskImage)
    {
        using Image<Rgba32> image = ISImage.Load<Rgba32>(photo);
        using Image<Rgba32> mask = ISImage.Load<Rgba32>(maskImage);
        if (mask.Width != image.Width || mask.Height != image.Height)
        {
            mask.Mutate(context => context.Resize(image.Width, image.Height));
        }
        Rgba32[] maskPixels = new Rgba32[mask.Width * mask.Height];
        mask.CopyPixelDataTo(maskPixels);
        int width = image.Width;
        image.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    row[x].A = (byte)(255 - maskPixels[y * width + x].R);
                }
            }
        });
        using MemoryStream output = new();
        image.Save(output, new PngEncoder() { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8 });
        return output.ToArray();
    }
}
