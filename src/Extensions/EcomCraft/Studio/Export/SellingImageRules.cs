using System.Globalization;
using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Rules for selling-point images (F47), independent of SwarmUI and fonts so they can be unit tested. The text is the user's own, drawn by the program
/// in fixed layouts; no AI writes or changes it.</summary>
public static class SellingImageRules
{
    /// <summary>Longest title, in characters.</summary>
    public const int MaxTitleLength = 20;

    /// <summary>Most selling points on one image.</summary>
    public const int MaxPoints = 3;

    /// <summary>Longest selling point, in characters.</summary>
    public const int MaxPointLength = 16;

    /// <summary>Accent color used when none is given.</summary>
    public const string DefaultAccent = "#1F2937";

    /// <summary>The layouts: a bottom banner, a left text column, or a top title with corner badges.</summary>
    public static readonly IReadOnlyList<string> Layouts = ["banner", "side", "badges"];

    /// <summary>A rectangle in pixels.</summary>
    public readonly record struct Box(float X, float Y, float Width, float Height);

    /// <summary>Where the text goes in a layout: the background panel (empty for none), the title area and the area for the selling points.</summary>
    public readonly record struct Plan(Box Panel, Box Title, Box Points, float TitleSize, float PointSize);

    /// <summary>Characters the bundled fonts cannot draw: emoji (surrogate pairs and the emoji presentation selector), private-use code points and the
    /// miscellaneous symbol and dingbat blocks, except the stars the fonts include. Common signs such as ℃, ™ or × stay allowed.</summary>
    private static readonly Regex Unsupported = new(@"[\p{Cs}\p{Co}️☀-☄☇-➿]", RegexOptions.CultureInvariant);

    /// <summary>One line of plain text: no control characters, collapsed whitespace.</summary>
    public static string CleanText(string text) =>
        Regex.Replace(Regex.Replace(text ?? "", @"\p{Cc}|\p{Cf}", " ", RegexOptions.CultureInvariant), @"\s+", " ", RegexOptions.CultureInvariant).Trim();

    /// <summary>Number of characters as people count them (a surrogate pair is one).</summary>
    public static int Length(string text) => new StringInfo(text ?? "").LengthInTextElements;

    /// <summary>The first character the fonts cannot draw, or null.</summary>
    public static string UnsupportedCharacter(string text)
    {
        Match match = Unsupported.Match(text ?? "");
        if (!match.Success)
        {
            return null;
        }
        return char.IsHighSurrogate(match.Value[0]) && match.Index + 1 < text.Length ? text.Substring(match.Index, 2) : match.Value;
    }

    /// <summary>Why the title and points cannot be used, or null. Expects cleaned text.</summary>
    public static string Problem(string title, IReadOnlyList<string> points)
    {
        if (string.IsNullOrEmpty(title))
        {
            return "请填写卖点图标题。";
        }
        if (Length(title) > MaxTitleLength)
        {
            return $"标题最多 {MaxTitleLength} 个字。";
        }
        if (points.Count > MaxPoints)
        {
            return $"卖点最多 {MaxPoints} 条。";
        }
        if (points.Any(point => Length(point) > MaxPointLength))
        {
            return $"每条卖点最多 {MaxPointLength} 个字。";
        }
        foreach (string text in points.Prepend(title))
        {
            if (UnsupportedCharacter(text) is string character)
            {
                return $"“{character}”这类表情或特殊符号无法排版，请删掉后重试。";
            }
        }
        return null;
    }

    /// <summary>A "#RRGGBB" accent color, or the default when the input is not one.</summary>
    public static string Accent(string hex) =>
        hex is not null && Regex.IsMatch(hex.Trim(), "^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant) ? hex.Trim().ToUpperInvariant() : DefaultAccent;

    /// <summary>White or near-black text, whichever reads better on the given "#RRGGBB" background (WCAG relative luminance).</summary>
    public static string TextOn(string hex)
    {
        int rgb = int.Parse(Accent(hex).TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        double channel(int value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * channel((rgb >> 16) & 255) + 0.7152 * channel((rgb >> 8) & 255) + 0.0722 * channel(rgb & 255);
        return luminance > 0.4 ? "#111111" : "#FFFFFF";
    }

    /// <summary>The text areas and starting font sizes of a layout on a picture of the given size. Sizes scale with the picture, so every resolution looks alike.</summary>
    public static Plan Layout(string layout, int width, int height)
    {
        float w = width, h = height;
        float unit = Math.Min(w, h);
        float margin = unit * 0.05f;
        switch (layout)
        {
            case "side":
            {
                float panelWidth = w * 0.38f;
                return new Plan(new Box(0, 0, panelWidth, h), new Box(margin, h * 0.12f, panelWidth - margin * 2, h * 0.3f),
                    new Box(margin, h * 0.45f, panelWidth - margin * 2, h * 0.45f), unit * 0.075f, unit * 0.04f);
            }
            case "badges":
            {
                return new Plan(new Box(0, 0, w, h * 0.17f), new Box(margin, h * 0.03f, w - margin * 2, h * 0.11f),
                    new Box(w * 0.45f, h * 0.62f, w * 0.55f - margin, h * 0.38f - margin), unit * 0.07f, unit * 0.038f);
            }
            default:
            {
                float top = h * 0.75f;
                return new Plan(new Box(0, top, w, h - top), new Box(margin, top + h * 0.03f, w - margin * 2, h * 0.1f),
                    new Box(margin, top + h * 0.14f, w - margin * 2, h * 0.08f), unit * 0.07f, unit * 0.036f);
            }
        }
    }

    /// <summary>The largest font size from <paramref name="start"/> down to <paramref name="min"/> (in 5% steps) at which <paramref name="measure"/>
    /// (size to width in pixels) fits <paramref name="maxWidth"/>; <paramref name="min"/> when nothing fits.</summary>
    public static float FitSize(Func<float, float> measure, float maxWidth, float start, float min)
    {
        for (float size = start; size >= min; size *= 0.95f)
        {
            if (measure(size) <= maxWidth)
            {
                return size;
            }
        }
        return min;
    }
}
