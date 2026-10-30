using System.IO;
using EcomCraftStudio.Studio.Storage;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>Product preservation (F02) as "local preprocessing → generation → local postprocessing" (R10.3): before generation the cut-out product is placed
/// on a canvas that becomes the template's product input; after generation the same cut-out is pasted back at the same placement, overwriting the saved works.
/// Both steps run on the platform server, so they stay local even if generation later moves to another executor. The cut-out is kept in memory only, for at most two hours.</summary>
public static class ProductPreserve
{
    /// <summary>Longest time a prepared cut-out waits for its generation.</summary>
    private const long KeepMs = 2 * 60 * 60 * 1000;

    /// <summary>Largest product photo accepted, in pixels; it is decoded only after this check.</summary>
    private const long MaxPixels = 100_000_000;

    /// <summary>Largest cut-out kept for compositing, in pixels: bigger ones (a phone photo can pass 40 megapixels under 5 MB) are scaled down first,
    /// which the result never shows, since the product is resized to its placement anyway.</summary>
    private const long WorkPixels = 16_000_000;

    /// <summary>Longest wait for a saved work to appear on disk.</summary>
    private static readonly TimeSpan SaveWait = TimeSpan.FromSeconds(60);

    /// <summary>A prepared request: owner, the cut-out as PNG, the placement and when it was made.</summary>
    private sealed record class Job(string UserId, byte[] CutoutPng, Placement Placement, long Created);

    /// <summary>Saved image types the product can be pasted into; videos and animations are skipped.</summary>
    private static readonly HashSet<string> PastableExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>Prepared requests by token.</summary>
    private static readonly ConcurrentDictionary<string, Job> Jobs = new();

    /// <summary>Builds the input canvas for a product photo with a transparent background and remembers the cut-out for the postprocessing.
    /// Returns a user-facing error, or null with the canvas as a PNG data URL and the token to pass to <see cref="ApplyAsync"/>.</summary>
    public static string Prepare(string userId, string productDataUrl, int width, int height, Placement placement, out string canvasDataUrl, out string token)
    {
        canvasDataUrl = null;
        token = null;
        Cleanup();
        int comma = productDataUrl?.IndexOf(',') ?? -1;
        if (comma < 0 || !productDataUrl.StartsWith("data:image/", StringComparison.Ordinal) || width < 64 || height < 64 || width > 8192 || height > 8192)
        {
            return "商品图无效，无法进行商品保真合成。";
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(productDataUrl[(comma + 1)..]);
            ImageInfo info = ISImage.Identify(bytes);
            if ((long)info.Width * info.Height > MaxPixels)
            {
                return "商品图尺寸过大，请缩小到 1 亿像素以内。";
            }
            using Image<Rgba32> cutout = ISImage.Load<Rgba32>(bytes);
            long pixels = (long)cutout.Width * cutout.Height;
            if (pixels > WorkPixels)
            {
                double scale = Math.Sqrt((double)WorkPixels / pixels);
                cutout.Mutate(context => context.Resize(Math.Max(1, (int)(cutout.Width * scale)), Math.Max(1, (int)(cutout.Height * scale))));
            }
            if (!ProductComposite.HasTransparency(cutout))
            {
                return "这个模板会把原商品像素贴回成图，需要去掉背景的商品图（透明背景的 PNG 或 WebP）。请先抠图后再上传。";
            }
            using Image<Rgba32> canvas = ProductComposite.BuildCanvas(cutout, width, height, placement);
            using MemoryStream canvasStream = new();
            canvas.Save(canvasStream, new PngEncoder());
            using MemoryStream cutoutStream = new();
            cutout.Save(cutoutStream, new PngEncoder());
            canvasDataUrl = "data:image/png;base64," + Convert.ToBase64String(canvasStream.ToArray());
            token = Guid.NewGuid().ToString("N");
            Jobs[token] = new Job(userId, cutoutStream.ToArray(), placement, Environment.TickCount64);
            return null;
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return "无法读取商品图，请重新导出为 PNG 或 WebP 后上传。";
        }
    }

    /// <summary>Drops prepared requests that were never used.</summary>
    private static void Cleanup()
    {
        long cutoff = Environment.TickCount64 - KeepMs;
        foreach (KeyValuePair<string, Job> pair in Jobs.Where(pair => pair.Value.Created < cutoff).ToArray())
        {
            Jobs.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>Forgets a prepared request whose generation did not start.</summary>
    public static void Discard(string token)
    {
        if (token is not null)
        {
            Jobs.TryRemove(token, out _);
        }
    }

    /// <summary>Pastes the product back onto every saved image of a successful generation and rewrites the files in place, keeping their embedded metadata.
    /// Videos are skipped. Returns how many images could not be processed; failures are logged and the unprocessed works stay as generated.</summary>
    public static async Task<int> ApplyAsync(string token, User user, JObject result)
    {
        if (token is null || !Jobs.TryRemove(token, out Job job) || job.UserId != user.UserID)
        {
            return 0;
        }
        int failed = 0;
        foreach (JToken image in result?["images"] as JArray ?? new JArray())
        {
            string url = image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString();
            string path = WorkSync.LocalPathFor(user, url, out _);
            if (path is null || !PastableExtensions.Contains(Path.GetExtension(path).ToLowerFast()))
            {
                continue;
            }
            if (!await WaitForSave(path))
            {
                failed++;
                continue;
            }
            try
            {
                await Task.Run(() => Rewrite(path, job));
                OutputMetadataTracker.RemoveMetadataFor(path.Replace('\\', '/'));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
            {
                Logs.Warning($"EcomCraft could not paste the product back into a generated image: {ex.Message}");
                failed++;
            }
        }
        return failed;
    }

    /// <summary>Waits until SwarmUI has finished writing the file.</summary>
    private static async Task<bool> WaitForSave(string path)
    {
        long deadline = Environment.TickCount64 + (long)SaveWait.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (File.Exists(path) && !Session.StillSavingFiles.ContainsKey(path) && !Session.StillSavingFiles.ContainsKey(path.Replace('\\', '/')))
            {
                return true;
            }
            await Task.Delay(250);
        }
        return false;
    }

    /// <summary>Loads the saved image, pastes the product back and saves it in the same format through a temporary file.</summary>
    private static void Rewrite(string path, Job job)
    {
        using Image<Rgba32> generated = ISImage.Load<Rgba32>(path);
        using Image<Rgba32> cutout = ISImage.Load<Rgba32>(job.CutoutPng);
        ProductComposite.PasteBack(generated, cutout, job.Placement);
        IImageFormat format = generated.Metadata.DecodedImageFormat ?? throw new NotSupportedException("Unknown image format.");
        string temp = $"{path}.ecomcraft-tmp";
        using (FileStream stream = File.Create(temp))
        {
            generated.Save(stream, format);
        }
        File.Move(temp, path, true);
    }
}
