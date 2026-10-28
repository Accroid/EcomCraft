using System.IO;
using System.IO.Compression;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Platform export (F05): the chosen works are scaled to fit the platform preset, padded with its background color, and either packed into a zip
/// with one folder per product, or written to the administrator's export location (R11.8). Clients send only their own gallery paths and group names,
/// never storage paths; zips are kept on the platform server for one hour.</summary>
[API.APIClass("EcomCraft platform export: resize and pad works to a platform preset, then download a zip or write them to the shared export folder.")]
public static class ExportService
{
    /// <summary>Most images in one export.</summary>
    public const int MaxImages = 200;

    /// <summary>How long a prepared zip stays downloadable.</summary>
    private const long KeepMs = 60 * 60 * 1000;

    /// <summary>Folder of prepared zips.</summary>
    private static string ZipDir => Path.Combine(Program.DataDir, "EcomCraft", "exports");

    /// <summary>The platforms that have a valid export preset, with their presets.</summary>
    public static List<(JObject Platform, ExportPreset Preset)> Presets()
    {
        List<(JObject, ExportPreset)> result = [];
        foreach (JObject platform in TemplateStore.Platforms())
        {
            ExportPreset preset = ExportRules.Parse(platform["export"] as JObject, out _);
            if (preset is not null)
            {
                result.Add((platform, preset));
            }
        }
        return result;
    }

    /// <summary>Encodes a rendered picture in the preset's format.</summary>
    private static byte[] Encode(Image<Rgb24> image, ExportPreset preset)
    {
        using MemoryStream stream = new();
        if (preset.Format == "png")
        {
            image.Save(stream, new PngEncoder());
        }
        else
        {
            image.Save(stream, new JpegEncoder() { Quality = preset.Quality });
        }
        return stream.ToArray();
    }

    [API.APIDescription("List the platforms that have an export preset (size, whether an administrator verified it) in the requested language, and whether exporting to the shared folder is available.", "platforms: [{ id: \"amazon\", name: \"\", width: 2000, height: 2000, verified: false }], folder_available: false")]
    public static Task<JObject> EcomCraftStudioExportOptions(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        JArray platforms = new();
        foreach ((JObject platform, ExportPreset preset) in Presets())
        {
            Dictionary<string, string> names = platform["name"]?.ToObject<Dictionary<string, string>>() ?? [];
            platforms.Add(new JObject()
            {
                ["id"] = platform["id"]?.ToString(),
                ["name"] = EcomTemplate.Pick(names, language),
                ["width"] = preset.Width,
                ["height"] = preset.Height,
                ["format"] = preset.Format,
                ["verified"] = preset.Verified
            });
        }
        return Task.FromResult(new JObject() { ["platforms"] = platforms, ["folder_available"] = !string.IsNullOrEmpty(StorageSettings.Get().ExportPath) });
    }

    [API.APIDescription("Export the current account's works for a platform: groups [{ name, images: [gallery path or result URL] }] (at most 200 images), target \"zip\" (returns a download URL) or \"folder\" (writes to the export location). rawInput is the root request JSON.", "download: \"/EcomCraft/export/…\", count: 4, verified: false")]
    public static async Task<JObject> EcomCraftStudioExport(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return StudioApi.Error("无权访问创作工作台。", "bad_permissions");
        }
        string platformId = rawInput.Value<string>("platform") ?? "";
        (JObject Platform, ExportPreset Preset) match = Presets().FirstOrDefault(item => item.Platform["id"]?.ToString() == platformId);
        if (match.Preset is null)
        {
            return StudioApi.Error("这个平台没有可用的导出规格。", "invalid_export");
        }
        string target = rawInput.Value<string>("target") == "folder" ? "folder" : "zip";
        string exportRoot = StorageSettings.Get().ExportPath;
        if (target == "folder" && string.IsNullOrEmpty(exportRoot))
        {
            return StudioApi.Error("管理员还没有设置导出位置，请改用打包下载。", "invalid_export");
        }
        List<(string Group, string Path)> files = [];
        foreach (JObject group in (rawInput["groups"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string name = ExportRules.SafeName(group.Value<string>("name"), "product");
            foreach (JToken image in group["images"] as JArray ?? new JArray())
            {
                string url = image.Type == JTokenType.String ? image.ToString() : null;
                if (url is not null && !url.StartsWith("View/", StringComparison.Ordinal) && !url.StartsWith("Output/", StringComparison.Ordinal))
                {
                    url = $"View/{session.User.UserID}/{url}";
                }
                string path = url is null ? null : WorkSync.LocalPathFor(session.User, url, out _);
                if (path is null || !File.Exists(path))
                {
                    return StudioApi.Error("有作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_export");
                }
                files.Add((name, path));
            }
        }
        if (files.Count == 0 || files.Count > MaxImages)
        {
            return StudioApi.Error($"请选择 1–{MaxImages} 张作品。", "invalid_export");
        }
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string label = $"{stamp}-{platformId}";
        try
        {
            return await Task.Run(() => target == "zip" ? WriteZip(session.User.UserID, label, files, match.Preset) : WriteFolder(session.User.UserID, exportRoot, label, files, match.Preset));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft export failed: {ex.Message}");
            return StudioApi.Error(target == "folder" ? "无法写入导出位置：共享文件夹可能离线或没有写入权限。" : "导出包没有生成，请稍后重试。", "export_failed");
        }
    }

    /// <summary>Packs the rendered works into a zip with one folder per group.</summary>
    private static JObject WriteZip(string userId, string label, List<(string Group, string Path)> files, ExportPreset preset)
    {
        Cleanup();
        List<string> notWhite = [];
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(ZipDir, userId);
        Directory.CreateDirectory(folder);
        string zipPath = Path.Combine(folder, $"{id}.zip");
        using (FileStream stream = File.Create(zipPath))
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] data) in Rendered(label, files, preset, notWhite))
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using Stream entryStream = entry.Open();
                entryStream.Write(data, 0, data.Length);
            }
        }
        return new JObject() { ["download"] = $"/EcomCraft/export/{id}", ["file_name"] = $"{label}.zip", ["count"] = files.Count, ["verified"] = preset.Verified, ["not_white"] = new JArray(notWhite) };
    }

    /// <summary>Writes the rendered works under the export location: export/&lt;account&gt;/&lt;time-platform&gt;/&lt;group&gt;/.</summary>
    private static JObject WriteFolder(string userId, string root, string label, List<(string Group, string Path)> files, ExportPreset preset)
    {
        List<string> notWhite = [];
        foreach ((string name, byte[] data) in Rendered(label, files, preset, notWhite))
        {
            string path = Path.Combine(root, userId, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }
        return new JObject() { ["exported"] = files.Count, ["folder"] = $"{userId}/{label}", ["verified"] = preset.Verified, ["not_white"] = new JArray(notWhite) };
    }

    /// <summary>Renders every work, naming them "&lt;label&gt;/&lt;group&gt;/&lt;n&gt;.&lt;ext&gt;" numbered within each group. AI provenance metadata is written
    /// from the work's own markers (F22), and works whose border is not pure white are added to <paramref name="notWhite"/> when the preset requires it.</summary>
    private static IEnumerable<(string Name, byte[] Data)> Rendered(string label, List<(string Group, string Path)> files, ExportPreset preset, List<string> notWhite)
    {
        Dictionary<string, int> counters = [];
        foreach ((string group, string path) in files)
        {
            int number = counters.GetValueOrDefault(group) + 1;
            counters[group] = number;
            string name = $"{label}/{group}/{number:D2}.{preset.Format}";
            using Image<Rgba32> source = Image.Load<Rgba32>(path);
            using Image<Rgb24> rendered = ExportImage.Render(source, preset);
            if (preset.RequireWhite && ComplianceRules.NonWhiteBorderFraction(rendered) > 0)
            {
                notWhite.Add($"{group}/{number:D2}.{preset.Format}");
            }
            string kind = ComplianceRules.SourceKind(MetadataText(source));
            string type = ComplianceRules.DigitalSourceType(kind);
            if (type is not null)
            {
                string description = ComplianceRules.Description(kind);
                rendered.Metadata.XmpProfile = new XmpProfile(Encoding.UTF8.GetBytes(ComplianceRules.Xmp(type, description)));
                rendered.Metadata.ExifProfile ??= new ExifProfile();
                rendered.Metadata.ExifProfile.SetValue(ExifTag.Software, ComplianceRules.CreatorTool);
                rendered.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, description);
            }
            yield return (name, Encode(rendered, preset));
        }
    }

    /// <summary>The SwarmUI metadata text of a saved work (PNG text chunks or the JPEG user comment), or an empty string.</summary>
    private static string MetadataText(Image<Rgba32> image)
    {
        StringBuilder text = new();
        foreach (SixLabors.ImageSharp.Formats.Png.Chunks.PngTextData item in image.Metadata.GetPngMetadata().TextData)
        {
            text.Append(item.Value);
        }
        if (image.Metadata.ExifProfile?.TryGetValue(ExifTag.UserComment, out IExifValue<EncodedString> comment) == true)
        {
            text.Append(comment.Value.Text);
        }
        return text.ToString();
    }

    /// <summary>The account's prepared zip, or null.</summary>
    public static string ZipPath(string userId, string id)
    {
        if (string.IsNullOrEmpty(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{32}$"))
        {
            return null;
        }
        string path = Path.Combine(ZipDir, userId, $"{id}.zip");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Deletes zips older than an hour.</summary>
    private static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(ZipDir))
            {
                return;
            }
            DateTime cutoff = DateTime.UtcNow.AddMilliseconds(-KeepMs);
            foreach (string file in Directory.EnumerateFiles(ZipDir, "*.zip", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Debug($"EcomCraft could not clean old exports: {ex.Message}");
        }
    }
}
