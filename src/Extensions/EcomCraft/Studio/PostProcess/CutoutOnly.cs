using System.IO;
using EcomCraftStudio.Studio.Export;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>The cut-out-only branch of main-image templates (F22): for platforms such as Amazon the product cut-out is centered on a pure white
/// (255, 255, 255) background without calling any generation model, and saved as a work in the user's gallery. The work is marked so exports
/// do not label it as AI-generated.</summary>
public static class CutoutOnly
{
    /// <summary>Share of the canvas's shorter side the product's longer side takes; Amazon asks for the product to fill about 85% of the frame.</summary>
    public const double ProductSize = 0.85;

    /// <summary>Makes the white-background picture from a cut-out data URL and saves it in the user's output folder. Returns a generation-like result or an error.</summary>
    public static JObject Create(User user, string cutoutDataUrl, int width, int height, string templateId)
    {
        int comma = cutoutDataUrl?.IndexOf(',') ?? -1;
        if (comma < 0)
        {
            return StudioApi.Error("缺少商品抠图。", "invalid_reference");
        }
        try
        {
            using Image<Rgba32> cutout = ISImage.Load<Rgba32>(Convert.FromBase64String(cutoutDataUrl[(comma + 1)..]));
            if (!ProductComposite.HasTransparency(cutout))
            {
                return StudioApi.Error("纯白主图需要去掉背景的商品图，请先完成抠图。", "invalid_reference");
            }
            using Image<Rgba32> trimmed = ProductComposite.Trim(cutout);
            using Image<Rgba32> canvas = ProductComposite.BuildCanvas(trimmed ?? cutout, width, height, new Placement(0.5, 0.5, ProductSize));
            JObject metadata = new()
            {
                ["sui_extra_data"] = new JObject()
                {
                    ["ecomcraft_cutout_only"] = "true",
                    ["ecomcraft_template"] = templateId,
                    ["ecomcraft_published_workflow"] = templateId,
                    ["ecomcraft_execution_server_name"] = "平台本机（纯抠图）"
                }
            };
            canvas.Metadata.GetPngMetadata().TextData.Add(new SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData("parameters", metadata.ToString(Formatting.None), "", ""));
            string relative = $"ecomcraft/main-white/{DateTime.Now:yyyy-MM-dd}/{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
            string path = Path.GetFullPath(UserImageHistoryHelper.GetRealPathFor(user, $"{user.OutputDirectory}/{relative}"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream stream = File.Create(path))
            {
                canvas.Save(stream, new PngEncoder());
            }
            string prefix = Program.ServerSettings.Paths.AppendUserNameToOutputPath ? $"View/{user.UserID}/" : "Output/";
            return new JObject() { ["images"] = new JArray(prefix + relative), ["execution_server"] = "平台本机（纯抠图）", ["cutout_only"] = true };
        }
        catch (Exception ex) when (ex is FormatException or UnknownImageFormatException or InvalidImageContentException or IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not make a white-background main image: {ex.Message}");
            return StudioApi.Error("纯白主图没有生成，请稍后重试。", "generation_failed");
        }
    }
}
