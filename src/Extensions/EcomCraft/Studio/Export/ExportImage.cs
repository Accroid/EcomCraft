using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Rendering of one export picture, independent of SwarmUI so padding and colors can be unit tested.</summary>
public static class ExportImage
{
    /// <summary>Renders one picture onto the preset canvas: fitted, centered, transparent areas showing the background color.</summary>
    public static Image<Rgb24> Render(Image<Rgba32> source, ExportPreset preset)
    {
        Image<Rgb24> canvas = new(preset.Width, preset.Height, new Rgb24(preset.R, preset.G, preset.B));
        (int X, int Y, int Width, int Height) rect = ExportRules.Fit(source.Width, source.Height, preset.Width, preset.Height);
        using Image<Rgba32> scaled = source.Clone(context => context.Resize(rect.Width, rect.Height));
        Rgba32[] pixels = new Rgba32[rect.Width * rect.Height];
        scaled.CopyPixelDataTo(pixels);
        canvas.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rect.Height; y++)
            {
                Span<Rgb24> row = rows.GetRowSpan(rect.Y + y);
                for (int x = 0; x < rect.Width; x++)
                {
                    Rgba32 pixel = pixels[y * rect.Width + x];
                    int a = pixel.A;
                    Rgb24 below = row[rect.X + x];
                    row[rect.X + x] = a == 255 ? new Rgb24(pixel.R, pixel.G, pixel.B)
                        : new Rgb24((byte)((pixel.R * a + below.R * (255 - a) + 127) / 255), (byte)((pixel.G * a + below.G * (255 - a) + 127) / 255), (byte)((pixel.B * a + below.B * (255 - a) + 127) / 255));
                }
            }
        });
        return canvas;
    }

}
