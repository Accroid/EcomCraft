using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.PostProcess;

namespace EcomCraftStudio.Studio.Export;

/// <summary>Rules for selling-point images (F47), independent of SwarmUI and fonts so they can be unit tested. The text is the user's own, drawn by the program
/// in fixed layouts; no AI writes or changes it.</summary>
public static class SellingImageRules
{
    /// <summary>Longest title, in characters: the product display name's own cap (T99); whether it fits a layout is checked when drawing (T100).</summary>
    public const int MaxTitleLength = 60;

    /// <summary>Most selling points on one image.</summary>
    public const int MaxPoints = 3;

    /// <summary>Longest selling point, in characters: a saved selling point's own cap (T99); whether it fits a layout is checked when drawing (T100).</summary>
    public const int MaxPointLength = 40;

    /// <summary>Most lines one title or point may wrap to before it counts as too long for the layout (T100): the font shrinks first, then the text wraps
    /// once; text that still does not fit is refused with a plain-language notice, never cut.</summary>
    public const int MaxLines = 2;

    /// <summary>Smallest font size, as a share of the layout's starting size, before text counts as too long (T100).</summary>
    public const float MinSizeShare = 0.6f;

    /// <summary>The layout that picks one of <see cref="Layouts"/> by where the product sits (T100).</summary>
    public const string AutoLayout = "auto";

    /// <summary>Accent color used when none is given.</summary>
    public const string DefaultAccent = "#1F2937";

    /// <summary>The layouts: a bottom banner, a left text column, a right text column, a top banner and a top title with corner badges (the right column and
    /// the top banner since T100, so a product on either side or at the bottom still leaves a layout clear of it). The order is the automatic choice's
    /// preference.</summary>
    public static readonly IReadOnlyList<string> Layouts = ["banner", "side", "right", "top", "badges"];

    /// <summary>Whether a request's layout is one of <see cref="Layouts"/> or the automatic choice.</summary>
    public static bool IsLayout(string layout) => layout == AutoLayout || Layouts.Contains(layout);

    /// <summary>The automatic layout choice (T100): the layout, and whether even the best one still covers part of the product (passed on as a notice).</summary>
    public sealed record class LayoutChoice(string Layout, bool OverlapsProduct);

    /// <summary>Text fitted into a width (T100): the font size and the lines it wraps to.</summary>
    public sealed record class FittedText(float Size, List<string> Lines);

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
            case "right":
            {
                float panelWidth = w * 0.38f;
                float left = w - panelWidth;
                return new Plan(new Box(left, 0, panelWidth, h), new Box(left + margin, h * 0.12f, panelWidth - margin * 2, h * 0.3f),
                    new Box(left + margin, h * 0.45f, panelWidth - margin * 2, h * 0.45f), unit * 0.075f, unit * 0.04f);
            }
            case "top":
            {
                float bottom = h * 0.25f;
                return new Plan(new Box(0, 0, w, bottom), new Box(margin, h * 0.03f, w - margin * 2, h * 0.1f),
                    new Box(margin, h * 0.14f, w - margin * 2, bottom - h * 0.15f), unit * 0.07f, unit * 0.036f);
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
                    new Box(margin, top + h * 0.14f, w - margin * 2, h - top - h * 0.15f), unit * 0.07f, unit * 0.036f);
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

    /// <summary>The areas a layout covers on the picture (T100): its panel, and for the corner badges also the badge area.</summary>
    public static List<Box> Covered(string layout, int width, int height)
    {
        Plan plan = Layout(layout, width, height);
        return layout == "badges" ? [plan.Panel, plan.Points] : [plan.Panel];
    }

    /// <summary>The product's box on a picture of the given size (T100): a square around the placement's center with the placement's size on the shorter
    /// side (the product's own aspect is not stored, so the square is never smaller than the product), widened by a small margin; null without a placement.</summary>
    public static Box? ProductBox(Placement product, int width, int height)
    {
        if (product is null)
        {
            return null;
        }
        float unit = Math.Min(width, height);
        float side = (float)(product.Size * unit) + unit * 0.06f;
        return new Box((float)(product.CenterX * width) - side / 2, (float)(product.CenterY * height) - side / 2, side, side);
    }

    /// <summary>The area two boxes share.</summary>
    private static float Overlap(Box a, Box b)
    {
        float width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
        float height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    /// <summary>Chooses the layout for the automatic choice (T100): the layout whose panels cover the least of the product's box, the earlier one in
    /// <see cref="Layouts"/> on a tie; without a known placement the bottom banner, as before. T83's free-space analysis can replace the box later.</summary>
    public static LayoutChoice ChooseLayout(int width, int height, Placement product)
    {
        if (ProductBox(product, width, height) is not Box box)
        {
            return new LayoutChoice(Layouts[0], false);
        }
        string best = Layouts[0];
        float bestOverlap = float.MaxValue;
        foreach (string layout in Layouts)
        {
            float overlap = Covered(layout, width, height).Sum(area => Overlap(area, box));
            if (overlap < bestOverlap - 0.5f)
            {
                best = layout;
                bestOverlap = overlap;
            }
        }
        return new LayoutChoice(best, bestOverlap > 0.5f);
    }

    /// <summary>The layouts in the order candidates try them (T100): the chosen one first, then the rest in preference order.</summary>
    public static List<string> LayoutOrder(string first) => [first, .. Layouts.Where(layout => layout != first)];

    /// <summary>Characters a line may break between on either side: CJK ideographs and punctuation, kana and full-width forms.</summary>
    private static readonly Regex BreakAnywhere = new(@"^[\p{IsCJKUnifiedIdeographs}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}\p{IsHiragana}\p{IsKatakana}]$",
        RegexOptions.CultureInvariant);

    /// <summary>The pieces a line may break between (T100): every CJK character on its own, runs of other characters together, and spaces as separate pieces.</summary>
    private static List<string> Pieces(string text)
    {
        List<string> pieces = [];
        StringBuilder run = new();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text ?? "");
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (element == " " || BreakAnywhere.IsMatch(element))
            {
                if (run.Length > 0)
                {
                    pieces.Add(run.ToString());
                    run.Clear();
                }
                pieces.Add(element);
                continue;
            }
            run.Append(element);
        }
        if (run.Length > 0)
        {
            pieces.Add(run.ToString());
        }
        return pieces;
    }

    /// <summary>Wraps text into at most <paramref name="maxLines"/> lines of <paramref name="maxWidth"/> pixels (T100), greedily: Chinese breaks between
    /// characters and other words at spaces; a single word wider than a line breaks between its characters. Null when the text needs more lines.</summary>
    public static List<string> WrapLines(string text, Func<string, float> measure, float maxWidth, int maxLines)
    {
        List<string> lines = [];
        string line = "";
        List<string> pieces = Pieces(text);
        for (int index = 0; index < pieces.Count; index++)
        {
            string piece = pieces[index];
            if (line.Length == 0 && piece == " ")
            {
                continue;
            }
            if (measure((line + piece).TrimEnd()) <= maxWidth)
            {
                line += piece;
                continue;
            }
            if (line.Trim().Length > 0)
            {
                lines.Add(line.TrimEnd());
                if (lines.Count >= maxLines)
                {
                    return null;
                }
                line = "";
                if (piece == " ")
                {
                    continue;
                }
                if (measure(piece) <= maxWidth)
                {
                    line = piece;
                    continue;
                }
            }
            // A word wider than a whole line: split it between its characters.
            StringInfo info = new(piece);
            if (info.LengthInTextElements <= 1)
            {
                return null;
            }
            List<string> split = [];
            for (int element = 0; element < info.LengthInTextElements; element++)
            {
                split.Add(info.SubstringByTextElements(element, 1));
            }
            pieces.RemoveAt(index);
            pieces.InsertRange(index, split);
            index--;
        }
        if (line.Trim().Length > 0)
        {
            lines.Add(line.TrimEnd());
        }
        return lines.Count <= maxLines ? lines : null;
    }

    /// <summary>Line height as a multiple of the font size, for fitting text into a box's height (T100).</summary>
    public const float LineHeight = 1.3f;

    /// <summary>Fits text into a box (T100): the largest size from <paramref name="start"/> down to <paramref name="min"/> (5% steps) on one line, else the
    /// largest size on up to <paramref name="maxLines"/> lines, always within <paramref name="maxHeight"/> at <see cref="LineHeight"/>; null when it does not
    /// fit even then, and the caller asks the user to shorten it instead of cutting it. <paramref name="measure"/> gives a string's width at a size.</summary>
    public static FittedText FitText(string text, Func<float, string, float> measure, float maxWidth, float maxHeight, float start, float min, int maxLines)
    {
        for (int lines = 1; lines <= maxLines; lines++)
        {
            for (float size = start; size >= min * 0.999f; size *= 0.95f)
            {
                if (lines * size * LineHeight > maxHeight)
                {
                    continue;
                }
                float current = size;
                List<string> wrapped = WrapLines(text, line => measure(current, line), maxWidth, lines);
                if (wrapped is not null)
                {
                    return new FittedText(size, wrapped);
                }
            }
        }
        return null;
    }

    /// <summary>The title and points a layout draws from a product's saved selling info (T98, F79): the display name (or the product's own name) and the
    /// first <see cref="MaxPoints"/> points, word for word: whether they fit is decided when drawing, and too-long text is refused, never shortened (T100).</summary>
    public static (string Title, List<string> Points) FromSpecs(Products.ProductSpecs specs, string productName)
    {
        string title = specs?.DisplayName;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = productName ?? "";
        }
        List<string> points = [.. (specs?.Points ?? []).Select(CleanText).Where(point => point.Length > 0).Take(MaxPoints)];
        return (CleanText(title), points);
    }
}
