using System.Text.RegularExpressions;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Rules for turning language-model output into prompt text (F37), independent of SwarmUI so they can be unit tested.
/// Model output only ever becomes plain prompt text: one line, no native directives, wildcards, quotes or labels, limited length.</summary>
public static class PromptRules
{
    /// <summary>Longest prompt text produced from model output.</summary>
    public const int MaxPromptLength = 1500;

    /// <summary>Labels models like to put in front of the answer.</summary>
    private static readonly Regex LeadingLabel = new(@"^\s*(prompt|positive prompt|english prompt|output|answer|提示词)\s*[:：]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Native prompt syntax the workspace refuses: &lt;…&gt; directives, __wildcards__ and {…|…} choices.</summary>
    private static readonly Regex NativeSyntax = new(@"<[^>]*>|__[^_\s]*__|[<>{}|]|__", RegexOptions.CultureInvariant);

    /// <summary>Reduces model output to safe prompt text, or an empty string when nothing usable remains.
    /// Keeps the first non-empty paragraph, drops labels, Markdown, quotes and native syntax, collapses whitespace and limits the length.</summary>
    public static string Sanitize(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }
        string text = output.Replace("\r", "");
        // Some models wrap the answer in a code block or think aloud first; keep the first real paragraph.
        text = Regex.Replace(text, @"```[a-zA-Z]*", "\n", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        string paragraph = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(part => part.Length > 0) ?? "";
        paragraph = paragraph.Replace("*", "").Replace("#", "").Replace("`", "");
        paragraph = LeadingLabel.Replace(paragraph, "");
        paragraph = NativeSyntax.Replace(paragraph, " ");
        paragraph = Regex.Replace(paragraph, @"\s+", " ", RegexOptions.CultureInvariant).Trim().Trim('"', '\'', '“', '”', '「', '」');
        if (paragraph.Length > MaxPromptLength)
        {
            int cut = paragraph.LastIndexOf(',', MaxPromptLength);
            paragraph = paragraph[..(cut > MaxPromptLength / 2 ? cut : MaxPromptLength)].TrimEnd(',', ' ');
        }
        return paragraph;
    }

    /// <summary>Whether text is plain ASCII, so it can be used as an English prompt without translation.</summary>
    public static bool IsAscii(string text) => !string.IsNullOrEmpty(text) && text.All(c => c < 128);

    /// <summary>The prompt subject: the model's text (or, without it, the user's description when it is already English) followed by the style fragment.
    /// Template prefixes and suffixes are added later by the template request, so they are not repeated here.</summary>
    public static string Compose(string modelText, string description, string styleFragment)
    {
        string subject = Sanitize(modelText);
        if (subject.Length == 0 && IsAscii(description?.Trim()))
        {
            subject = Sanitize(description);
        }
        return string.Join(", ", new[] { subject, styleFragment?.Trim() }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>Cleans a prompt the user typed into the bilingual prompt panel (F54): line breaks become spaces, then the same rules as model output apply,
    /// so native directives, wildcards and labels can never reach the engine.</summary>
    public static string CleanUserPrompt(string text) => Sanitize(Regex.Replace(text ?? "", @"\s+", " ", RegexOptions.CultureInvariant));

    /// <summary>Which panel text decides the engine prompt (F54): "en" when the English text is given (sent as is), "zh" when only the Chinese text is given
    /// (translated first, unless it is already plain ASCII), or "" when the panel is empty and the one-line description applies.</summary>
    public static string PanelSource(string promptEn, string promptZh)
    {
        if (CleanUserPrompt(promptEn).Length > 0)
        {
            return "en";
        }
        return CleanUserPrompt(promptZh).Length > 0 ? "zh" : "";
    }

    /// <summary>The instruction for "AI expand" (F54): turn the one-line description (or, without one, the product type) into a fuller English description
    /// of the product photo. The product-type, model and style fragments are still added by the server, so the answer only has to describe the picture.</summary>
    public static string ExpandInstruction(string styleName, string productTypeName, bool needModel) =>
        "You write prompts for an e-commerce product photo generator. Expand the user's short description (which may be Chinese or English, or may only name the product) "
        + "into one detailed English prompt of about 30 to 60 words describing the product, scene, props, lighting and composition. "
        + "Keep the product itself unchanged and do not invent brand names or text on the product. "
        + (string.IsNullOrWhiteSpace(productTypeName) ? "" : $"The product is: {productTypeName}. ")
        + (needModel ? "A model presents the product. " : "")
        + (string.IsNullOrWhiteSpace(styleName) ? "" : $"The chosen visual style is: {styleName}. ")
        + "Reply with the prompt only, on one line, without quotes, labels or explanations.";

    /// <summary>The instruction for translating a product-photo prompt between the panel's languages (F54); the translation only ever becomes prompt text.</summary>
    public static string TranslateInstruction(string targetLanguage) =>
        $"Translate the user's e-commerce product photo prompt into {targetLanguage}. Keep every detail and do not add new ones. "
        + "Reply with the translation only, on one line, without quotes, labels or explanations.";

    /// <summary>The instruction sent with the user's description: write a short English product-photo prompt and nothing else.</summary>
    public static string Instruction(string styleName) =>
        "You write prompts for an e-commerce product photo generator. Rewrite the user's description (which may be Chinese or English) into one concise English prompt "
        + "describing the product, scene, lighting and composition. Keep the product itself unchanged and do not invent brand names or text on the product. "
        + (string.IsNullOrWhiteSpace(styleName) ? "" : $"The chosen visual style is: {styleName}. ")
        + "Reply with the prompt only, on one line, without quotes, labels or explanations.";
}
