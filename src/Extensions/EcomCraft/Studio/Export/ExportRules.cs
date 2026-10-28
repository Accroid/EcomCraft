using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Export;

/// <summary>One platform's export preset (F05): output size, padding color, file format and whether an administrator checked it against the platform's current rules.</summary>
public sealed record class ExportPreset(int Width, int Height, byte R, byte G, byte B, string Format, int Quality, bool Verified, string Source, bool RequireWhite = false);

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
        if (width < 64 || height < 64 || width > 8192 || height > 8192)
        {
            error = "导出尺寸须在 64–8192 像素之间。";
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
}
