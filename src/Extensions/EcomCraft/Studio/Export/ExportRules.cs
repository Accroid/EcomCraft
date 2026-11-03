using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Export;

/// <summary>One platform's export preset (F05): output size, padding color, file format and whether an administrator checked it against the platform's current rules.</summary>
public sealed record class ExportPreset(int Width, int Height, byte R, byte G, byte B, string Format, int Quality, bool Verified, string Source, bool RequireWhite = false);

/// <summary>One image slot of a platform (F58): the preset that renders it, plus slot-only rules — a per-file size cap, a minimum subject fill, the suggested
/// position of the white image in the platform's image list and whether the slot joins image sets only when ticked. Detail screens (T97) also carry
/// long-image stitching rules, which are export rules only, never output sizes.</summary>
public sealed record class ExportSlot(string Id, string Kind, ExportPreset Preset, long MaxBytes, double SubjectRatioMin, int Position, bool Optional, LongImageRules LongImage = null)
{
    /// <summary>The slot kinds templates can serve; "white_main" also implies the pure-white background check. Main-image kinds first (T97): white bottom,
    /// first image, scene, selling point, detail, model; then detail screens; "video_cover" only records video specs.</summary>
    public static readonly string[] Kinds = ["white_main", "first_main", "scene_main", "selling", "detail", "model", "detail_page", "video_cover"];

    /// <summary>The slot kinds of the main-image group (F58, T97): the pictures shoppers browse at the top of a listing.</summary>
    public static readonly string[] MainGroupKinds = ["white_main", "first_main", "scene_main", "selling", "detail", "model"];

    /// <summary>The slot kinds of the detail-page group (T97): single screens of the long description, stitched per the slot's long-image rules.</summary>
    public static readonly string[] DetailGroupKinds = ["detail_page"];

    /// <summary>A slot kind's group for set and export grouping (T97): "main", "detail", or "record" for kinds that only note specs, such as video covers.</summary>
    public static string GroupOf(string kind) => MainGroupKinds.Contains(kind) ? "main" : DetailGroupKinds.Contains(kind) ? "detail" : "record";

    /// <summary>Whether the slot's pictures must have a pure white border (F22).</summary>
    public bool RequireWhite => Preset.RequireWhite || Kind == "white_main";
}

/// <summary>Long-image stitching rules of a detail-screen slot (T97): whether the platform accepts one whole long picture, how tall it may be and how many
/// single-screen slices may follow. Export rules only — the long image height is not an output size, so it is not bound by the output size cap; unset
/// values are 0 and mean the official rules say nothing about them.</summary>
public sealed record class LongImageRules(bool Allowed, int MaxHeight, int MaxSlices);

/// <summary>Export rules independent of SwarmUI so sizes, padding and names can be unit tested.</summary>
public static class ExportRules
{
    /// <summary>Reads a platform's "export" object. Returns null with a reason when a value is missing or out of range.</summary>
    public static ExportPreset Parse(JObject export, out string error)
    {
        error = null;
        if (export is null)
        {
            error = "没有导出规格。";
            return null;
        }
        int width = export.Value<int?>("width") ?? 0;
        int height = export.Value<int?>("height") ?? 0;
        string background = export.Value<string>("background") ?? "#FFFFFF";
        string rawFormat = export.Value<string>("format") ?? "jpg";
        string format = string.Equals(rawFormat, "png", StringComparison.OrdinalIgnoreCase) ? "png" : string.Equals(rawFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : null;
        int quality = export.Value<int?>("quality") ?? 92;
        if (width < 64 || height < 64 || width > 2048 || height > 2048)
        {
            error = "导出尺寸须在 64–2048 像素之间。";
            return null;
        }
        if (!Regex.IsMatch(background, "^#[0-9A-Fa-f]{6}$"))
        {
            error = "背景色须为 #RRGGBB 形式。";
            return null;
        }
        if (format is null)
        {
            error = "格式须为 jpg 或 png。";
            return null;
        }
        if (quality < 50 || quality > 100)
        {
            error = "JPEG 质量须在 50–100 之间。";
            return null;
        }
        byte channel(int index) => byte.Parse(background.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new ExportPreset(width, height, channel(1), channel(3), channel(5), format, quality, export.Value<bool?>("verified") == true, export.Value<string>("source") ?? "",
            export.Value<bool?>("require_white_background") == true);
    }

    /// <summary>Where a picture goes inside the export canvas: scaled to fit entirely (up or down), keeping its aspect ratio, centered; the rest is padding.</summary>
    public static (int X, int Y, int Width, int Height) Fit(int sourceWidth, int sourceHeight, int width, int height)
    {
        double scale = Math.Min((double)width / Math.Max(1, sourceWidth), (double)height / Math.Max(1, sourceHeight));
        int w = Math.Clamp((int)Math.Round(sourceWidth * scale), 1, width);
        int h = Math.Clamp((int)Math.Round(sourceHeight * scale), 1, height);
        return ((width - w) / 2, (height - h) / 2, w, h);
    }

    /// <summary>A folder or file name made safe for zip entries and shared folders: no path separators or reserved characters, at most 60 characters.</summary>
    public static string SafeName(string name, string fallback)
    {
        string text = Regex.Replace(name ?? "", @"[\\/:*?""<>|\x00-\x1F]", "_").Trim().Trim('.');
        if (text.Length > 60)
        {
            text = text[..60].Trim();
        }
        return text.Length == 0 ? fallback : text;
    }

    /// <summary>Reads a platform's slots (F58): the v3 "slots" list, or a single slot built from the v2 "export" object (kind "white_main" when it requires a
    /// pure white background, else "scene_main"). Returns null with a reason when the platform has no usable slot; a bad slot is skipped with its reason appended.</summary>
    public static List<ExportSlot> ParseSlots(JObject platform, out string error)
    {
        error = null;
        if (platform is null)
        {
            error = "没有平台数据。";
            return null;
        }
        List<ExportSlot> slots = [];
        if (platform["slots"] is JArray slotList)
        {
            foreach (JObject slot in slotList.OfType<JObject>())
            {
                ExportSlot parsed = ParseSlot(slot, out string slotError);
                if (parsed is not null)
                {
                    slots.Add(parsed);
                }
                else if (error is null)
                {
                    error = slotError;
                }
            }
            if (slots.Count == 0)
            {
                error = error ?? "slots 中没有可用的槽位。";
                return null;
            }
            if (slots.Select(slot => slot.Id).Distinct().Count() != slots.Count)
            {
                error = "槽位 ID 重复。";
                return null;
            }
            return slots;
        }
        ExportPreset preset = Parse(platform["export"] as JObject, out string exportError);
        if (preset is null)
        {
            error = exportError;
            return null;
        }
        return [new ExportSlot("main", preset.RequireWhite ? "white_main" : "scene_main", preset, 0, 0, 0, false)];
    }

    /// <summary>Reads one slot: the same size, background, format and quality fields as a v2 preset, plus the slot-only rules. Returns null with a reason when invalid.</summary>
    private static ExportSlot ParseSlot(JObject slot, out string error)
    {
        error = null;
        string id = slot["id"]?.ToString() ?? "";
        string kind = slot["kind"]?.ToString() ?? "";
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]{0,31}$"))
        {
            error = $"槽位 ID 无效：{id}。";
            return null;
        }
        if (!ExportSlot.Kinds.Contains(kind))
        {
            error = $"槽位 {id} 的 kind 无效，须为 {string.Join(" / ", ExportSlot.Kinds)}。";
            return null;
        }
        ExportPreset preset = Parse(slot, out string presetError);
        if (preset is null)
        {
            error = $"槽位 {id}：{presetError}";
            return null;
        }
        long maxBytes = slot["max_bytes"]?.Type == JTokenType.Integer ? slot.Value<long>("max_bytes") : 0;
        double subjectRatio = slot["subject_ratio_min"]?.Type == JTokenType.Float || slot["subject_ratio_min"]?.Type == JTokenType.Integer ? slot.Value<double>("subject_ratio_min") : 0;
        int position = slot["position"]?.Type == JTokenType.Integer ? slot.Value<int>("position") : 0;
        if (maxBytes < 0 || maxBytes > 100 * 1024 * 1024)
        {
            error = $"槽位 {id} 的大小上限须在 0–100MB 之间。";
            return null;
        }
        if (subjectRatio < 0 || subjectRatio > 1)
        {
            error = $"槽位 {id} 的主体占比须在 0–1 之间。";
            return null;
        }
        if (position < 0 || position > 20)
        {
            error = $"槽位 {id} 的建议位置须在 0–20 之间。";
            return null;
        }
        bool requireWhite = slot.Value<bool?>("require_white") == true;
        LongImageRules longImage = ParseLongImage(slot, preset, out string longImageError);
        if (longImageError is not null)
        {
            error = $"槽位 {id}：{longImageError}";
            return null;
        }
        return new ExportSlot(id, kind, preset with { RequireWhite = requireWhite }, maxBytes, subjectRatio, position, slot.Value<bool?>("optional") == true, longImage);
    }

    /// <summary>Reads a detail screen's long-image stitching rules (T97), only on "detail_page" slots. Returns null with a reason when the object sits on another
    /// kind or a value is out of range; a slot without a "long_image" object has none.</summary>
    private static LongImageRules ParseLongImage(JObject slot, ExportPreset preset, out string error)
    {
        error = null;
        if (slot["long_image"] is not JObject raw)
        {
            return null;
        }
        if ((slot["kind"]?.ToString() ?? "") != "detail_page")
        {
            error = "long_image 只能用于 detail_page 槽位。";
            return null;
        }
        bool allowed = raw.Value<bool?>("allowed") == true;
        int maxHeight = raw["max_height"]?.Type == JTokenType.Integer ? raw.Value<int>("max_height") : 0;
        int maxSlices = raw["max_slices"]?.Type == JTokenType.Integer ? raw.Value<int>("max_slices") : 0;
        if (maxHeight > 0 && (maxHeight < preset.Height || maxHeight > 100000))
        {
            error = "长图最大高度须不低于单屏高度且不超过 100000。";
            return null;
        }
        if (maxSlices > 0 && (maxSlices < 1 || maxSlices > 100))
        {
            error = "切片数量上限须在 1–100 之间。";
            return null;
        }
        if (!allowed && maxHeight > 0)
        {
            error = "未允许整张长图时不接受 max_height。";
            return null;
        }
        return new LongImageRules(allowed, maxHeight, maxSlices);
    }

    /// <summary>The JPEG qualities to try when a slot has a size cap (F58): the preset's own quality, then a midpoint, then the floor — two re-encodes at most.</summary>
    public static int[] QualityLadder(int quality)
    {
        if (quality <= 50)
        {
            return [50];
        }
        int middle = Math.Max(50, (quality + 50) / 2);
        return middle <= 50 ? [quality, 50] : [quality, middle, 50];
    }
}
