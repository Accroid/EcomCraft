using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio.Products;

/// <summary>One name-and-value parameter of a product's selling info (F79, T99), written by the user.</summary>
public sealed class ProductParam
{
    /// <summary>Parameter name, for example "容量".</summary>
    public string Name = "";

    /// <summary>Parameter value, for example "380 ml".</summary>
    public string Value = "";
}

/// <summary>The selling info of one product in one language (F79, T99): a display name, up to 5 selling points, parameters and a size line.
/// Kept per language (at least zh-CN and en) because overseas listings are written by the user, never machine-translated; an old record without
/// info simply has none of this filled in.</summary>
public sealed class ProductSpecs
{
    /// <summary>Display name for listings in this language, or empty when not filled in.</summary>
    public string DisplayName = "";

    /// <summary>Selling points in this language.</summary>
    public List<string> Points = [];

    /// <summary>Name-and-value parameters in this language.</summary>
    public List<ProductParam> Params = [];

    /// <summary>Size line in this language, for example "380 ml / 高 12 cm", or empty.</summary>
    public string Size = "";
}

/// <summary>The rules of a product's selling info (F79, T99): which languages exist and the length and count caps of every field. Pure rules,
/// independent of SwarmUI, so they can be unit tested; the server cleans with them before saving and the page mirrors the same caps.</summary>
public static class ProductInfoRules
{
    /// <summary>The languages selling info can be kept in: the home market's Chinese and English for overseas listings.</summary>
    public static readonly string[] KnownLanguages = ["zh-CN", "en"];

    /// <summary>Longest display name.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Most selling points.</summary>
    public const int MaxPoints = 5;

    /// <summary>Longest selling point.</summary>
    public const int MaxPointLength = 40;

    /// <summary>Most parameters.</summary>
    public const int MaxParams = 20;

    /// <summary>Longest parameter name.</summary>
    public const int MaxParamNameLength = 30;

    /// <summary>Longest parameter value.</summary>
    public const int MaxParamValueLength = 60;

    /// <summary>Longest size line.</summary>
    public const int MaxSizeLength = 60;

    /// <summary>The known language ID as written ("zh-CN" or "en"), or null for anything else.</summary>
    public static string LanguageId(string language) => KnownLanguages.Contains(language) ? language : null;

    /// <summary>A cleaned one-line text: control characters and extra spaces removed and trimmed. Returns the cleaned text when it fits within
    /// <paramref name="maxLength"/> characters (empty when nothing is left), or null when it is too long, so callers can say which field failed.</summary>
    private static string CleanLine(string text, int maxLength)
    {
        string cleaned = Regex.Replace(text ?? "", @"[\p{C}\r\n]+", " ", RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
        return cleaned.Length > maxLength ? null : cleaned;
    }

    /// <summary>Cleans the selling info of one language into what may be saved: fields are trimmed, control characters removed and caps enforced.
    /// Returns the cleaned specs and null, or the previous value untouched and a plain-language reason when a field is too long or there are too
    /// many entries. Empty fields stay empty, so a language can be saved partly filled or cleared.</summary>
    public static (ProductSpecs Cleaned, string Error) CleanSpecs(ProductSpecs specs)
    {
        specs ??= new ProductSpecs();
        if (specs.Points.Count > MaxPoints)
        {
            return (null, $"卖点最多 {MaxPoints} 条。");
        }
        if (specs.Params.Count > MaxParams)
        {
            return (null, $"参数最多 {MaxParams} 组。");
        }
        string name = CleanLine(specs.DisplayName, MaxNameLength);
        if (name is null)
        {
            return (null, $"展示名最多 {MaxNameLength} 个字符。");
        }
        List<string> points = [];
        foreach (string point in specs.Points)
        {
            string cleaned = CleanLine(point, MaxPointLength);
            if (cleaned is null)
            {
                return (null, $"每条卖点最多 {MaxPointLength} 个字符。");
            }
            points.Add(cleaned);
        }
        List<ProductParam> parameters = [];
        foreach (ProductParam param in specs.Params)
        {
            if (param is null)
            {
                continue;
            }
            string paramName = CleanLine(param.Name, MaxParamNameLength);
            string paramValue = CleanLine(param.Value, MaxParamValueLength);
            if (paramName is null || paramValue is null)
            {
                return (null, $"参数名最多 {MaxParamNameLength} 个字符，参数值最多 {MaxParamValueLength} 个字符。");
            }
            if (paramName.Length > 0 && paramValue.Length > 0)
            {
                parameters.Add(new ProductParam() { Name = paramName, Value = paramValue });
            }
        }
        string size = CleanLine(specs.Size, MaxSizeLength);
        if (size is null)
        {
            return (null, $"尺寸最多 {MaxSizeLength} 个字符。");
        }
        return (new ProductSpecs() { DisplayName = name, Points = points, Params = parameters, Size = size }, null);
    }

    /// <summary>Whether a language's info holds anything worth keeping, so empty entries are never saved.</summary>
    public static bool IsFilled(ProductSpecs specs) =>
        specs is not null && (!string.IsNullOrEmpty(specs.DisplayName) || specs.Points.Count > 0 || (specs.Params?.Count ?? 0) > 0 || !string.IsNullOrEmpty(specs.Size));

    /// <summary>The instruction for suggesting selling points from the user's own description (F79, T99): the model may only summarize what the
    /// user wrote — parameters, certifications, effects, sales figures and rankings must never be added, because the listing must not promise
    /// anything the seller cannot prove.</summary>
    public static string SuggestInstruction(string languageName) =>
        "You write selling points for an e-commerce product listing. Summarize ONLY what the user's text actually says about the product. "
        + "Never invent: do not add parameters, certifications, test results, effects or benefits, sales figures or rankings the user did not write. "
        + $"Reply with 3 to 5 selling points in {languageName}, one per line, each at most 20 characters, without numbering, bullets, quotes or explanations.";

    /// <summary>Reads the suggested selling points from the model's answer (F79, T99): lines are taken as points, numbering and bullets are
    /// stripped, and each point is trimmed and capped at <see cref="MaxPointLength"/> characters; empty lines and duplicates are dropped and at
    /// most <see cref="MaxPoints"/> points are kept.</summary>
    public static List<string> ParseSuggestedPoints(string answer)
    {
        string text = (answer ?? "").Replace("\r", "");
        // Code fences and think-aloud blocks are dropped first; the rest is cleaned line by line below.
        text = Regex.Replace(text, @"```[a-zA-Z]*", "\n", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        List<string> points = [];
        foreach (string raw in text.Split('\n'))
        {
            string point = Regex.Replace(raw, @"<[^>]*>", " ", RegexOptions.CultureInvariant);
            point = Regex.Replace(point, @"__[^_\s]*__", " ", RegexOptions.CultureInvariant);
            point = point.Replace("*", "").Replace("#", "").Replace("`", "");
            point = Regex.Replace(point, @"^[\s\-–—•*·]+", "", RegexOptions.CultureInvariant);
            point = Regex.Replace(point, @"^\d+[\.、\)]\s*", "", RegexOptions.CultureInvariant);
            point = Regex.Replace(point, @"[\p{C}]+", " ", RegexOptions.CultureInvariant);
            point = Regex.Replace(point, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
            if (point.Length == 0)
            {
                continue;
            }
            if (point.Length > MaxPointLength)
            {
                point = point[..MaxPointLength].Trim();
            }
            if (point.Length > 0 && !points.Contains(point, StringComparer.Ordinal))
            {
                points.Add(point);
            }
        }
        return [.. points.Take(MaxPoints)];
    }
}
