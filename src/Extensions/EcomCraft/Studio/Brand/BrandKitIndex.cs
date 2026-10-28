using System.Globalization;
using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio.Brand;

/// <summary>One saved brand kit (F12): who owns it, its name, brand colors, preferred background and the files of its logo and style references.
/// File locations never leave the server.</summary>
public sealed class BrandKitRecord
{
    /// <summary>Brand kit ID.</summary>
    public string Id;

    /// <summary>Owner account.</summary>
    public string UserId;

    /// <summary>Display name.</summary>
    public string Name;

    /// <summary>Creation time, Unix milliseconds.</summary>
    public long Created;

    /// <summary>Folder holding the files.</summary>
    public string Folder;

    /// <summary>Brand colors as "#RRGGBB".</summary>
    public List<string> Colors = [];

    /// <summary>The preferred background as the user wrote it (Chinese or English).</summary>
    public string Background = "";

    /// <summary>The English prompt text of the background (translated once when saved), or empty when it is not usable.</summary>
    public string BackgroundPrompt = "";

    /// <summary>File name of the logo, or null.</summary>
    public string Logo;

    /// <summary>File names of the style reference images, in order.</summary>
    public List<string> References = [];
}

/// <summary>The brand kit index with per-account isolation and the rules that turn a kit into prompt text, independent of SwarmUI so they can be unit tested.
/// Every lookup takes the account: another account's kit is never found, changed or removed.</summary>
public sealed class BrandKitIndex
{
    /// <summary>Longest kit name.</summary>
    public const int MaxNameLength = 40;

    /// <summary>Most kits per account.</summary>
    public const int MaxPerUser = 20;

    /// <summary>Most brand colors per kit.</summary>
    public const int MaxColors = 5;

    /// <summary>Most style reference images per kit.</summary>
    public const int MaxReferences = 4;

    /// <summary>Longest preferred-background text.</summary>
    public const int MaxBackgroundLength = 200;

    /// <summary>Pattern of stored file names: "logo" or "ref-" plus 8 hex digits, and an image extension. Anything else is never served.</summary>
    public static readonly Regex FileName = new("^(logo|ref-[0-9a-f]{8})\\.(png|jpg|webp)$", RegexOptions.CultureInvariant);

    /// <summary>All records.</summary>
    public List<BrandKitRecord> Items = [];

    /// <summary>The account's kits, newest first.</summary>
    public List<BrandKitRecord> ForUser(string userId) => [.. Items.Where(item => item.UserId == userId).OrderByDescending(item => item.Created)];

    /// <summary>The account's kit with this ID, or null; another account's kit is never returned.</summary>
    public BrandKitRecord Find(string userId, string id) =>
        string.IsNullOrEmpty(id) ? null : Items.FirstOrDefault(item => item.UserId == userId && item.Id == id);

    /// <summary>Adds a kit; returns a reason when the account already has the most kits allowed.</summary>
    public string Add(BrandKitRecord record)
    {
        if (Items.Count(item => item.UserId == record.UserId) >= MaxPerUser)
        {
            return $"每个账号最多 {MaxPerUser} 个品牌资料包，请先删除不用的。";
        }
        Items.Add(record);
        return null;
    }

    /// <summary>Removes the account's kit and returns it, or null when there is none.</summary>
    public BrandKitRecord Remove(string userId, string id)
    {
        BrandKitRecord record = Find(userId, id);
        if (record is not null)
        {
            Items.Remove(record);
        }
        return record;
    }

    /// <summary>A cleaned kit name (one line, no control characters, limited length), or null when nothing is left.</summary>
    public static string CleanName(string name)
    {
        string cleaned = Regex.Replace(name ?? "", @"[\p{C}<>]", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        if (cleaned.Length > MaxNameLength)
        {
            cleaned = cleaned[..MaxNameLength].TrimEnd();
        }
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>Brand colors normalized to upper-case "#RRGGBB" without duplicates, or null when one is not a color or there are too many.</summary>
    public static List<string> CleanColors(IEnumerable<string> colors)
    {
        List<string> result = [];
        foreach (string color in colors ?? Enumerable.Empty<string>())
        {
            string value = (color ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(value, "^#[0-9A-F]{6}$", RegexOptions.CultureInvariant))
            {
                return null;
            }
            if (!result.Contains(value))
            {
                result.Add(value);
            }
        }
        return result.Count > MaxColors ? null : result;
    }

    /// <summary>A plain English color word for "#RRGGBB" that an image model understands, such as "navy blue", "light pink" or "dark gray".</summary>
    public static string ColorName(string hex)
    {
        int rgb = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double r = ((rgb >> 16) & 255) / 255.0, g = ((rgb >> 8) & 255) / 255.0, b = (rgb & 255) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2;
        double delta = max - min;
        double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        if (saturation < 0.15 || delta < 0.06)
        {
            return lightness > 0.92 ? "white" : lightness > 0.7 ? "light gray" : lightness > 0.35 ? "gray" : lightness > 0.12 ? "dark gray" : "black";
        }
        double hue = max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (hue < 0)
        {
            hue += 360;
        }
        if (hue >= 15 && hue < 45 && lightness < 0.4)
        {
            return "brown";
        }
        if (hue >= 200 && hue < 255 && lightness < 0.3)
        {
            return "navy blue";
        }
        string name = hue < 15 || hue >= 345 ? (lightness > 0.7 ? "pink" : "red")
            : hue < 45 ? "orange" : hue < 70 ? "yellow" : hue < 160 ? "green" : hue < 200 ? "teal" : hue < 255 ? "blue"
            : hue < 290 ? "purple" : "magenta";
        if (name == "pink")
        {
            return name;
        }
        return lightness > 0.75 ? $"light {name}" : lightness < 0.25 ? $"dark {name}" : name;
    }

    /// <summary>The kit's English prompt fragment: its color words and preferred background, or an empty string.</summary>
    public static string PromptFragment(BrandKitRecord record)
    {
        if (record is null)
        {
            return "";
        }
        List<string> names = [.. record.Colors.Select(ColorName).Distinct()];
        string colors = names.Count == 0 ? "" : names.Count == 1 ? $"brand color {names[0]}" : $"brand colors {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}";
        return string.Join(", ", new[] { colors, record.BackgroundPrompt?.Trim() }.Where(part => !string.IsNullOrEmpty(part)));
    }
}
