using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

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

    /// <summary>Photo-type phrases that name the product, kept as a neutral photo type ("product shot" becomes "still life shot").</summary>
    private static readonly Regex ProductPhotoPhrase = new(@"\bproduct\s+(shot|photo|photograph|photography|image|display|showcase)s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Words that make the image model draw a product of its own.</summary>
    private static readonly Regex ProductWord = new(@"\b(products?|bottles?|jars?|tubes?|box|boxes|packages?|packaging|containers?|pumps?|dispensers?|caps?|lids?|labels?|logos?|brands?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Removes what would make the image model draw a product of its own from model-written prompt text (F02/F59): the real product is
    /// pasted in after generation, and small models often ignore the instruction not to describe it. Product photo phrases become "still life",
    /// and every comma clause that still names a product, container or label is dropped. Returns an empty string when nothing is left.</summary>
    public static string WithoutProduct(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        text = ProductPhotoPhrase.Replace(text, match => "still life " + match.Groups[1].Value.ToLowerInvariant());
        List<string> sentences = [];
        foreach (string sentence in Regex.Split(text.Trim(), @"(?<=[.!?])\s+", RegexOptions.CultureInvariant))
        {
            string body = sentence.TrimEnd('.', '!', '?', ' ');
            List<string> clauses = [.. body.Split(',').Select(clause => clause.Trim()).Where(clause => clause.Length > 0 && !ProductWord.IsMatch(clause))];
            if (clauses.Count == 0)
            {
                continue;
            }
            // Joining clauses can leave a sentence starting with "and"/"with"; it still reads well enough for the image model.
            string kept = string.Join(", ", clauses);
            sentences.Add(char.ToUpperInvariant(kept[0]) + kept[1..] + ".");
        }
        return string.Join(" ", sentences);
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

    /// <summary>Whether composing the prompt has to wait for the language model (F56): Chinese panel text to translate, or with an empty panel a one-line
    /// description to rewrite. Edited English, an expanded prompt left unchanged (sent as English) and plain ASCII panel text skip the model.</summary>
    public static bool NeedsModel(string description, string promptEn, string promptZh)
    {
        string source = PanelSource(promptEn, promptZh);
        if (source == "en")
        {
            return false;
        }
        if (source == "zh")
        {
            return !IsAscii(CleanUserPrompt(promptZh));
        }
        return !string.IsNullOrWhiteSpace(description);
    }

    /// <summary>What prompts must leave out: the real product is already in the picture and is pasted back after generation (F02),
    /// so a product named or described in the prompt gets drawn as a second one next to it.</summary>
    private const string SceneOnly = "The real product is already in the picture, so never describe the product or name its type, and never add "
        + "bottles, jars, tubes, boxes, packaging or other products; describe only the surface it stands on and everything around it. ";

    /// <summary>The instruction for "AI expand" (F54): turn the one-line description (or, without one, the product type) into a fuller English description
    /// of the product photo. The product-type, model and style fragments are still added by the server, so the answer only has to describe the picture.</summary>
    public static string ExpandInstruction(string styleName, string productTypeName, bool needModel) =>
        "You write prompts for an e-commerce product photo generator. Expand the user's short description (which may be Chinese or English, or may only name the product) "
        + "into one detailed English prompt of about 30 to 60 words describing the scene, props, lighting and composition. "
        + SceneOnly
        + (string.IsNullOrWhiteSpace(productTypeName) ? "" : $"The product, only for choosing a fitting scene, is: {productTypeName}. ")
        + (needModel ? "A model appears in the scene. " : "")
        + (string.IsNullOrWhiteSpace(styleName) ? "" : $"The chosen visual style is: {styleName}. ")
        + "Reply with the prompt only, on one line, without quotes, labels or explanations.";

    /// <summary>The instruction for translating a product-photo prompt between the panel's languages (F54); the translation only ever becomes prompt text.</summary>
    public static string TranslateInstruction(string targetLanguage) =>
        $"Translate the user's e-commerce product photo prompt into {targetLanguage}. Keep every detail and do not add new ones. "
        + "Reply with the translation only, on one line, without quotes, labels or explanations.";

    /// <summary>Whether image description for reference images is on (decision A5, F68): on unless the administrator explicitly turned it off.</summary>
    public static bool VisionOn(JObject settings) => settings?.Value<bool?>("vision_enabled") != false;

    /// <summary>Whether product photos may be sent to the vision model to describe the product (decision A5): only after an administrator explicitly allowed it.</summary>
    public static bool ProductVisionOn(JObject settings) => settings?.Value<bool?>("product_vision_enabled") == true;

    /// <summary>The state of image description shown to users (F68): "off" (turned off), "no_model" (no vision model set), "unreachable"
    /// (the vision service cannot be reached; <paramref name="available"/> is null), "model_missing" (reachable but the model is not installed) or "ready".
    /// Ollama names without a tag mean ":latest".</summary>
    public static string VisionState(bool enabled, string model, IEnumerable<string> available)
    {
        if (!enabled)
        {
            return "off";
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            return "no_model";
        }
        if (available is null)
        {
            return "unreachable";
        }
        string wanted = WithTag(model);
        return available.Any(name => !string.IsNullOrWhiteSpace(name) && WithTag(name) == wanted) ? "ready" : "model_missing";
    }

    /// <summary>A model name in lower case with Ollama's default ":latest" tag added when it has none.</summary>
    private static string WithTag(string name)
    {
        string lower = name.Trim().ToLowerInvariant();
        return lower.Contains(':') ? lower : lower + ":latest";
    }

    /// <summary>The instruction sent with the user's description: write a short English product-photo prompt and nothing else.</summary>
    public static string Instruction(string styleName) =>
        "You write prompts for an e-commerce product photo generator. Rewrite the user's description (which may be Chinese or English) into one concise English prompt "
        + "describing the scene, lighting and composition. " + SceneOnly
        + (string.IsNullOrWhiteSpace(styleName) ? "" : $"The chosen visual style is: {styleName}. ")
        + "Reply with the prompt only, on one line, without quotes, labels or explanations.";
}
