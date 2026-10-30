using System.IO;
using System.IO.Compression;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using ISImage = SixLabors.ImageSharp.Image;
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

    /// <summary>One platform's export data (F58): its JSON, verified flag, source note and parsed slots.</summary>
    public sealed record class PlatformExport(JObject Platform, List<ExportSlot> Slots, bool Verified, string Source)
    {
        /// <summary>The slot exports go to when the request does not name one: the first non-optional slot, else the first.</summary>
        public ExportSlot DefaultSlot => Slots.FirstOrDefault(slot => !slot.Optional) ?? Slots[0];

        /// <summary>The slot with the given ID, or null.</summary>
        public ExportSlot Find(string id) => Slots.FirstOrDefault(slot => slot.Id == id);
    }

    /// <summary>The platforms that have a usable slot, with their slots (F58).</summary>
    public static List<PlatformExport> PlatformPresets()
    {
        List<PlatformExport> result = [];
        foreach (JObject platform in TemplateStore.Platforms())
        {
            List<ExportSlot> slots = ExportRules.ParseSlots(platform, out _);
            if (slots is not null)
            {
                bool verified = platform.Value<bool?>("verified") == true || (platform["export"] as JObject)?.Value<bool?>("verified") == true;
                string source = platform.Value<string>("source") ?? (platform["export"] as JObject)?.Value<string>("source") ?? "";
                result.Add(new PlatformExport(platform, slots, verified, source));
            }
        }
        return result;
    }

    /// <summary>The size of a platform's slot for an image kind (white_main, scene_main, detail, model; F58), the non-optional slot first; null when the
    /// platform or the kind has none. It is the output size chosen by default before generating.</summary>
    public static (int Width, int Height)? SlotSize(string platformId, string kind)
    {
        if (string.IsNullOrEmpty(platformId) || string.IsNullOrEmpty(kind))
        {
            return null;
        }
        PlatformExport match = PlatformPresets().FirstOrDefault(item => item.Platform["id"]?.ToString() == platformId);
        ExportSlot slot = match?.Slots.Where(item => item.Kind == kind).OrderBy(item => item.Optional).FirstOrDefault();
        return slot is null ? null : (slot.Preset.Width, slot.Preset.Height);
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

    /// <summary>Encodes within the slot's size cap when it has one (F58): JPEGs walk the quality ladder (two re-encodes at most); PNGs encode once.
    /// <paramref name="overCap"/> says whether the result is still over the cap, so the export can say so instead of refusing.</summary>
    private static byte[] Encode(Image<Rgb24> image, ExportSlot slot, out bool overCap)
    {
        overCap = false;
        if (slot.MaxBytes <= 0)
        {
            return Encode(image, slot.Preset);
        }
        byte[] best = null;
        foreach (int quality in ExportRules.QualityLadder(slot.Preset.Quality))
        {
            best = Encode(image, slot.Preset.Format == "png" ? slot.Preset : slot.Preset with { Quality = quality });
            if (best.Length <= slot.MaxBytes)
            {
                return best;
            }
        }
        overCap = true;
        return best;
    }

    [API.APIDescription("List the platforms that have an export slot (slots with size and optional flag, whether an administrator verified them) in the requested language, and whether exporting to the shared folder is available.", "platforms: [{ id: \"amazon\", name: \"\", width: 2000, height: 2000, verified: false, slots: [{ id: \"white_main\", kind: \"white_main\", optional: false }], main_image_count: 9, ai_declaration: true }], folder_available: false")]
    public static Task<JObject> EcomCraftStudioExportOptions(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        JArray platforms = new();
        foreach (PlatformExport platform in PlatformPresets())
        {
            Dictionary<string, string> names = platform.Platform["name"]?.ToObject<Dictionary<string, string>>() ?? [];
            ExportSlot main = platform.DefaultSlot;
            JArray slots = new();
            foreach (ExportSlot slot in platform.Slots)
            {
                Dictionary<string, string> slotNames = (platform.Platform["slots"] as JArray)?.OfType<JObject>()
                    .FirstOrDefault(item => item["id"]?.ToString() == slot.Id)?["name"]?.ToObject<Dictionary<string, string>>() ?? [];
                string slotName = EcomTemplate.Pick(slotNames, language);
                slots.Add(new JObject()
                {
                    ["id"] = slot.Id,
                    ["kind"] = slot.Kind,
                    ["name"] = string.IsNullOrEmpty(slotName) ? slot.Id : slotName,
                    ["width"] = slot.Preset.Width,
                    ["height"] = slot.Preset.Height,
                    ["optional"] = slot.Optional,
                    ["position"] = slot.Position,
                    ["max_bytes"] = slot.MaxBytes,
                    ["subject_ratio_min"] = slot.SubjectRatioMin
                });
            }
            platforms.Add(new JObject()
            {
                ["id"] = platform.Platform["id"]?.ToString(),
                ["name"] = EcomTemplate.Pick(names, language),
                ["width"] = main.Preset.Width,
                ["height"] = main.Preset.Height,
                ["format"] = main.Preset.Format,
                ["verified"] = platform.Verified,
                ["slots"] = slots,
                ["main_image_count"] = platform.Platform.Value<int?>("main_image_count") ?? 0,
                ["ai_declaration"] = (platform.Platform["ai_label"] as JObject)?.Value<bool?>("platform_declaration") == true
            });
        }
        return Task.FromResult(new JObject() { ["platforms"] = platforms, ["folder_available"] = !string.IsNullOrEmpty(StorageSettings.Get().ExportPath) });
    }

    [API.APIDescription("Export the current account's works for a platform: groups [{ name, images: [gallery path, result URL or { url, slot }] }] (at most 200 images), target \"zip\" (returns a download URL) or \"folder\" (writes to the export location), and the optional slot ID every image goes to when the entries do not name one (F58). Warnings come back as codes the page words in its language. rawInput is the root request JSON.", "download: \"/EcomCraft/export/…\", count: 4, verified: false, warnings: [{ code: \"size_over\", slot: \"white_main\", count: 1 }]")]
    public static async Task<JObject> EcomCraftStudioExport(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return StudioApi.Error("无权访问创作工作台。", "bad_permissions");
        }
        string platformId = rawInput.Value<string>("platform") ?? "";
        PlatformExport match = PlatformPresets().FirstOrDefault(item => item.Platform["id"]?.ToString() == platformId);
        if (match is null)
        {
            return StudioApi.Error("这个平台没有可用的导出规格。", "invalid_export");
        }
        string defaultSlotId = rawInput.Value<string>("slot") ?? "";
        ExportSlot defaultSlot = defaultSlotId.Length == 0 ? match.DefaultSlot : match.Find(defaultSlotId);
        if (defaultSlot is null)
        {
            return StudioApi.Error("所选槽位不存在，请重新选择。", "invalid_export");
        }
        string target = rawInput.Value<string>("target") == "folder" ? "folder" : "zip";
        // Works made at a size chosen before generating are downloaded at that size; only format and size cap follow the platform.
        bool keepSize = rawInput.Value<bool?>("keep_size") == true;
        string exportRoot = StorageSettings.Get().ExportPath;
        if (target == "folder" && string.IsNullOrEmpty(exportRoot))
        {
            return StudioApi.Error("管理员还没有设置导出位置，请改用打包下载。", "invalid_export");
        }
        List<FileEntry> files = [];
        foreach (JObject group in (rawInput["groups"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string name = ExportRules.SafeName(group.Value<string>("name"), "product");
            foreach (JToken image in group["images"] as JArray ?? new JArray())
            {
                string url = image.Type == JTokenType.String ? image.ToString() : image is JObject wrapped && wrapped["url"]?.Type == JTokenType.String ? wrapped.Value<string>("url") : null;
                if (url is not null && !url.StartsWith("View/", StringComparison.Ordinal) && !url.StartsWith("Output/", StringComparison.Ordinal))
                {
                    url = $"View/{session.User.UserID}/{url}";
                }
                string path = url is null ? null : WorkSync.LocalPathFor(session.User, url, out _);
                if (path is null || !File.Exists(path))
                {
                    return StudioApi.Error("有作品不存在或不属于当前账号，请刷新后重新选择。", "invalid_export");
                }
                string slotId = image is JObject withSlot && withSlot["slot"]?.Type == JTokenType.String ? withSlot.Value<string>("slot") : "";
                ExportSlot slot = slotId.Length == 0 ? defaultSlot : match.Find(slotId);
                if (slot is null)
                {
                    return StudioApi.Error("所选槽位不存在，请重新选择。", "invalid_export");
                }
                files.Add(new FileEntry(name, path, slot, keepSize));
            }
        }
        if (files.Count == 0 || files.Count > MaxImages)
        {
            return StudioApi.Error($"请选择 1–{MaxImages} 张作品。", "invalid_export");
        }
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string label = $"{stamp}-{platformId}";
        // Exported works are kept forever (F39).
        foreach (JObject group in rawInput["groups"] as JArray ?? new JArray())
        {
            CandidateCleanup.MarkKept(session.User, group["images"] as JArray);
        }
        try
        {
            return await Task.Run(() => target == "zip" ? WriteZip(session.User.UserID, label, files, match) : WriteFolder(session.User.UserID, exportRoot, label, files, match));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            Logs.Warning($"EcomCraft export failed: {ex.Message}");
            return StudioApi.Error(target == "folder" ? "无法写入导出位置：共享文件夹可能离线或没有写入权限。" : "导出包没有生成，请稍后重试。", "export_failed");
        }
    }

    /// <summary>One work to export, with the slot it goes to (F58), and whether it keeps its own size (the size was chosen before generating).</summary>
    private sealed record class FileEntry(string Group, string Path, ExportSlot Slot, bool KeepSize);

    /// <summary>The slot of a work that keeps its own size: format, quality, size cap and white check come from the platform's slot for the work's image kind
    /// (read from its template marker), else from <paramref name="fallback"/>; the size is the work's own, so it is not scaled or padded.</summary>
    private static ExportSlot KeepSizeSlot(PlatformExport platform, ExportSlot fallback, Image<Rgba32> source)
    {
        System.Text.RegularExpressions.Match marker = System.Text.RegularExpressions.Regex.Match(MetadataText(source), @"""ecomcraft_template""\s*:\s*""([A-Za-z0-9_.\-]+)""");
        string kind = marker.Success ? TemplateStore.Find(marker.Groups[1].Value)?.Template?.Kind : null;
        ExportSlot slot = (string.IsNullOrEmpty(kind) ? null : platform.Slots.Where(item => item.Kind == kind).OrderBy(item => item.Optional).FirstOrDefault()) ?? fallback;
        return slot with { Preset = slot.Preset with { Width = source.Width, Height = source.Height } };
    }

    /// <summary>Export hints (F58), counted per code and slot; the page words them in the UI language. Codes: size_over (still over the slot's cap after
    /// re-encoding), subject_small (below a white slot's subject fill) and text_image (a selling-point image on a platform that forbids promotional text).</summary>
    private sealed class ExportWarnings
    {
        /// <summary>Counts per code and slot.</summary>
        private readonly Dictionary<(string Code, string Slot), int> Counts = [];

        /// <summary>Records one hint.</summary>
        public void Add(string code, string slot)
        {
            Counts[(code, slot)] = Counts.GetValueOrDefault((code, slot)) + 1;
        }

        /// <summary>The hints as JSON, ordered for stable display.</summary>
        public JArray ToJson()
        {
            return [.. Counts.OrderBy(item => item.Key.Code).ThenBy(item => item.Key.Slot)
                .Select(item => new JObject() { ["code"] = item.Key.Code, ["slot"] = item.Key.Slot, ["count"] = item.Value })];
        }
    }

    /// <summary>Packs the rendered works into a zip with one folder per group.</summary>
    private static JObject WriteZip(string userId, string label, List<FileEntry> files, PlatformExport platform)
    {
        Cleanup();
        List<string> notWhite = [];
        ExportWarnings warnings = new();
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(ZipDir, userId);
        Directory.CreateDirectory(folder);
        string zipPath = Path.Combine(folder, $"{id}.zip");
        using (FileStream stream = File.Create(zipPath))
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] data) in Rendered(label, files, platform, notWhite, warnings))
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using Stream entryStream = entry.Open();
                entryStream.Write(data, 0, data.Length);
            }
        }
        return new JObject() { ["download"] = $"/EcomCraft/export/{id}", ["file_name"] = $"{label}.zip", ["count"] = files.Count, ["verified"] = platform.Verified, ["not_white"] = new JArray(notWhite), ["warnings"] = warnings.ToJson(), ["ai_declaration"] = (platform.Platform["ai_label"] as JObject)?.Value<bool?>("platform_declaration") == true };
    }

    /// <summary>Writes the rendered works under the export location: export/&lt;account&gt;/&lt;time-platform&gt;/&lt;group&gt;/.</summary>
    private static JObject WriteFolder(string userId, string root, string label, List<FileEntry> files, PlatformExport platform)
    {
        List<string> notWhite = [];
        ExportWarnings warnings = new();
        foreach ((string name, byte[] data) in Rendered(label, files, platform, notWhite, warnings))
        {
            string path = Path.Combine(root, userId, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }
        return new JObject() { ["exported"] = files.Count, ["folder"] = $"{userId}/{label}", ["verified"] = platform.Verified, ["not_white"] = new JArray(notWhite), ["warnings"] = warnings.ToJson(), ["ai_declaration"] = (platform.Platform["ai_label"] as JObject)?.Value<bool?>("platform_declaration") == true };
    }

    /// <summary>Renders every work to its slot (F58), naming them "&lt;label&gt;/&lt;group&gt;/&lt;n&gt;.&lt;ext&gt;" numbered within each group. AI provenance metadata is written
    /// from the work's own markers (F22); hints collect into <paramref name="warnings"/> (over-cap, low subject fill, text image) and works whose border is
    /// not pure white are added to <paramref name="notWhite"/> when the slot requires it.</summary>
    private static IEnumerable<(string Name, byte[] Data)> Rendered(string label, List<FileEntry> files, PlatformExport platform, List<string> notWhite, ExportWarnings warnings)
    {
        Dictionary<string, int> counters = [];
        bool forbidsPromoText = ComplianceRules.ForbidsPromoText(platform.Platform);
        foreach (FileEntry file in files)
        {
            int number = counters.GetValueOrDefault(file.Group) + 1;
            counters[file.Group] = number;
            using Image<Rgba32> source = ISImage.Load<Rgba32>(file.Path);
            ExportSlot slot = file.KeepSize ? KeepSizeSlot(platform, file.Slot, source) : file.Slot;
            string name = $"{label}/{file.Group}/{number:D2}.{slot.Preset.Format}";
            using Image<Rgb24> rendered = ExportImage.Render(source, slot.Preset);
            if (slot.RequireWhite && ComplianceRules.NonWhiteBorderFraction(rendered) > 0)
            {
                notWhite.Add($"{file.Group}/{number:D2}.{slot.Preset.Format}");
            }
            if (slot.SubjectRatioMin > 0 && slot.Kind == "white_main" && ComplianceRules.SubjectFillFraction(rendered) < slot.SubjectRatioMin)
            {
                warnings.Add("subject_small", slot.Id);
            }
            if (forbidsPromoText && MetadataText(source).Contains("ecomcraft_selling_image", StringComparison.Ordinal))
            {
                warnings.Add("text_image", slot.Id);
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
            byte[] data = Encode(rendered, file.Slot, out bool overCap);
            if (overCap)
            {
                warnings.Add("size_over", slot.Id);
            }
            yield return (name, data);
        }
    }

    /// <summary>The SwarmUI metadata text of a saved work (PNG text chunks or the JPEG user comment), or an empty string.</summary>
    internal static string MetadataText(Image<Rgba32> image)
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
