using System.IO;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Scales generated works to the output size chosen before generating (local postprocessing, R10.3): pictures are generated at the same aspect ratio
/// near one megapixel (<see cref="OutputSizeRules"/>) and rewritten in place at the exact size, keeping their format and embedded metadata. Runs before
/// product preservation, so the original product is pasted back at the final size and never scaled with the picture.</summary>
public static class OutputResize
{
    /// <summary>Saved image types that are resized; videos and animations are skipped.</summary>
    private static readonly HashSet<string> ResizableExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Rewrites every saved image of a successful generation at the exact size. Returns how many images could not be processed;
    /// failures are logged and those works stay at the generated size.</summary>
    public static async Task<int> ApplyAsync(User user, JObject result, int width, int height)
    {
        int failed = 0;
        foreach (JToken image in result?["images"] as JArray ?? new JArray())
        {
            string url = image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString();
            string path = WorkSync.LocalPathFor(user, url, out _);
            if (path is null || !ResizableExtensions.Contains(Path.GetExtension(path).ToLowerFast()))
            {
                continue;
            }
            if (!await ProductPreserve.WaitForSave(path))
            {
                failed++;
                continue;
            }
            try
            {
                await Task.Run(() => Rewrite(path, width, height));
                OutputMetadataTracker.RemoveMetadataFor(path.Replace('\\', '/'));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                Logs.Warning($"EcomCraft could not scale a generated image to the chosen size: {ex.Message}");
                failed++;
            }
        }
        return failed;
    }

    /// <summary>Scales the saved image to cover the size, crops the center and saves it in the same format through a temporary file.</summary>
    private static void Rewrite(string path, int width, int height)
    {
        using Image<Rgba32> generated = ISImage.Load<Rgba32>(path);
        if (generated.Width == width && generated.Height == height)
        {
            return;
        }
        (int scaledWidth, int scaledHeight, int cropX, int cropY) = OutputSizeRules.Cover(generated.Width, generated.Height, width, height);
        generated.Mutate(context => context.Resize(scaledWidth, scaledHeight).Crop(new Rectangle(cropX, cropY, width, height)));
        IImageFormat format = generated.Metadata.DecodedImageFormat ?? throw new NotSupportedException("Unknown image format.");
        string temp = $"{path}.ecomcraft-tmp";
        using (FileStream stream = File.Create(temp))
        {
            generated.Save(stream, format);
        }
        File.Move(temp, path, true);
    }
}
