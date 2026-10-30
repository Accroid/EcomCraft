using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Rules for selling points and set storyboards (F25), independent of SwarmUI so they can be unit tested.
/// The language model only answers in a fixed line format; every shot is checked against the templates the server allows and its prompt fragment
/// is cleaned like any other prompt text, so the model can never choose a workflow, parameter or execution server.</summary>
public static class StoryboardRules
{
    /// <summary>Most selling points in one storyboard.</summary>
    public const int MaxPoints = 5;

    /// <summary>Most shots (images per product) in one storyboard.</summary>
    public const int MaxShots = 8;

    /// <summary>Longest selling point.</summary>
    public const int MaxPointLength = 60;

    /// <summary>Longest purpose text of a shot.</summary>
    public const int MaxPurposeLength = 60;

    /// <summary>Longest prompt fragment of a shot.</summary>
    public const int MaxFragmentLength = 300;

    /// <summary>One image of the storyboard: the template that makes it, the English prompt fragment it adds and what it is for.</summary>
    public sealed record class Shot(string TemplateId, string Prompt, string Purpose);

    /// <summary>A storyboard: selling points in the user's language and the shots.</summary>
    public sealed record class Storyboard(List<string> Points, List<Shot> Shots);

    /// <summary>"POINT: …" lines, also accepting a Chinese label.</summary>
    private static readonly Regex PointLine = new(@"^(point|selling point|卖点)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"SHOT: template | prompt | purpose" lines.</summary>
    private static readonly Regex ShotLine = new(@"^(shot|分镜)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>List markers models like to put in front of lines.</summary>
    private static readonly Regex ListMarker = new(@"^\s*([-*•·]|\d+[.)、])\s*", RegexOptions.CultureInvariant);

    /// <summary>Plain one-line display text (selling points, purposes): no control characters, no native prompt syntax, collapsed whitespace, limited length.</summary>
    public static string CleanText(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        string cleaned = Regex.Replace(text, @"[\p{C}<>{}|`*#]", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.CultureInvariant).Trim().Trim('"', '\'', '“', '”', '「', '」');
        return cleaned.Length > maxLength ? cleaned[..maxLength].TrimEnd() : cleaned;
    }

    /// <summary>Cleans a shot's prompt fragment like any other prompt text and keeps it short, cutting at a comma when possible.</summary>
    public static string CleanFragment(string text)
    {
        string fragment = PromptRules.CleanUserPrompt(text);
        if (fragment.Length > MaxFragmentLength)
        {
            int cut = fragment.LastIndexOf(',', MaxFragmentLength);
            fragment = fragment[..(cut > MaxFragmentLength / 2 ? cut : MaxFragmentLength)].TrimEnd(',', ' ');
        }
        return fragment;
    }

    /// <summary>Cleans user selling points: empty ones dropped, duplicates removed, at most <see cref="MaxPoints"/>.</summary>
    public static List<string> CleanPoints(IEnumerable<string> points)
    {
        List<string> result = [];
        foreach (string point in points ?? Enumerable.Empty<string>())
        {
            string cleaned = CleanText(point, MaxPointLength);
            if (cleaned.Length > 0 && !result.Contains(cleaned) && result.Count < MaxPoints)
            {
                result.Add(cleaned);
            }
        }
        return result;
    }

    /// <summary>The instruction for writing a storyboard: answer only in POINT / SHOT lines, choosing templates from the given list.</summary>
    public static string Instruction(string languageName, IEnumerable<(string Id, string Name)> templates, string productName, string styleName, bool needModel) =>
        "You plan the image set of an e-commerce product listing. From the user's product description and optional selling points, "
        + $"first list up to {MaxPoints} selling points, then plan up to {MaxShots} images, each highlighting one selling point. "
        + "Choose each image's template only from this list (id: name): "
        + string.Join("; ", templates.Select(template => $"{template.Id}: {template.Name}")) + ". "
        + (string.IsNullOrWhiteSpace(productName) ? "" : $"The product is: {productName}. ")
        + (needModel ? "A model appears in the scene. " : "")
        + (string.IsNullOrWhiteSpace(styleName) ? "" : $"The chosen visual style is: {styleName}. ")
        + "The real product is already in each picture, so the prompt fragments never describe the product or name its type, and never add bottles, jars, "
        + "tubes, boxes, packaging or other products. Answer only with lines in exactly this format and nothing else:\n"
        + $"POINT: <selling point in {languageName}>\n"
        + $"SHOT: <template id> | <short English prompt fragment describing scene, props, lighting and composition> | <purpose of the image in {languageName}>";

    /// <summary>Reads a storyboard from the model's answer. Lines in other formats, unknown templates and empty fragments are dropped;
    /// at most <see cref="MaxPoints"/> points and <see cref="MaxShots"/> shots are kept.</summary>
    public static Storyboard Parse(string output, ISet<string> allowedTemplates)
    {
        List<string> points = [];
        List<Shot> shots = [];
        if (string.IsNullOrWhiteSpace(output))
        {
            return new Storyboard(points, shots);
        }
        string text = Regex.Replace(output, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (string rawLine in text.Replace("\r", "").Split('\n'))
        {
            string line = ListMarker.Replace(rawLine.Replace("*", "").Replace("`", "").Trim(), "");
            Match point = PointLine.Match(line);
            if (point.Success)
            {
                string cleaned = CleanText(point.Groups[2].Value, MaxPointLength);
                if (cleaned.Length > 0 && !points.Contains(cleaned) && points.Count < MaxPoints)
                {
                    points.Add(cleaned);
                }
                continue;
            }
            Match shot = ShotLine.Match(line);
            if (!shot.Success || shots.Count >= MaxShots)
            {
                continue;
            }
            string[] parts = shot.Groups[2].Value.Split('|');
            if (parts.Length < 2)
            {
                continue;
            }
            string templateId = parts[0].Trim().Trim('"', '\'', '<', '>', '[', ']');
            string fragment = CleanFragment(parts[1]);
            string purpose = parts.Length > 2 ? CleanText(string.Join(" ", parts[2..]), MaxPurposeLength) : "";
            if (!allowedTemplates.Contains(templateId) || fragment.Length == 0)
            {
                continue;
            }
            shots.Add(new Shot(templateId, fragment, purpose));
        }
        return new Storyboard(points, shots);
    }

    /// <summary>The storyboard used when the model is unavailable or gave nothing usable: one shot per set template, without fragments, named after the template.</summary>
    public static List<Shot> Fallback(IEnumerable<(string Id, string Name)> setTemplates) =>
        [.. setTemplates.Take(MaxShots).Select(template => new Shot(template.Id, "", CleanText(template.Name, MaxPurposeLength)))];
}
