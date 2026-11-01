using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.MagicPrompt.WebAPI.Models;
using EcomCraftStudio.Studio.Common;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Templates;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Prompting;


/// <summary>Prompt composition, expansion and translation. Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
    /// <summary>Composes the prompt subject for a product type, an optional style, the optional model flag and a description (F37/F53).
    /// The template's own prefix and suffix are added by the template request.
    /// <paramref name="productImage"/> (a data URL) is used only when administrators explicitly allowed product photo description (A5).</summary>
    public static async Task<Composed> ComposeAsync(Session session, string styleId, string productTypeId, bool needModel, string description, string productImage)
    {
        Style style = FindStyle(styleId);
        string fragment = Fragment(style, productTypeId, needModel);
        description = description?.Trim() ?? "";
        string productHint = "";
        if (!string.IsNullOrEmpty(productImage) && ProductVisionEnabled())
        {
            productHint = await AskModelAsync(session, "Describe the main product in this photo in one short English phrase (type, color, material). Reply with the phrase only.",
                "Describe the product.", productImage);
        }
        if (description.Length == 0 && productHint.Length == 0)
        {
            return new Composed(PromptRules.Compose("", "", fragment), "template", null, null);
        }
        string text = string.Join(". ", new[] { productHint, description }.Where(part => part.Length > 0));
        string answer = PromptRules.WithoutProduct(await AskModelAsync(session, PromptRules.Instruction(style is null ? null : EcomTemplate.Pick(style.Name, "en")), text, null));
        if (answer.Length > 0)
        {
            return new Composed(PromptRules.Compose(answer, "", fragment), "model", null, null);
        }
        bool usedDescription = PromptRules.IsAscii(description);
        string notice = usedDescription
            ? "提示词优化服务暂时不可用，已直接使用你的英文描述和所选风格。"
            : "提示词优化服务暂时不可用，已使用模板和风格的默认描述；你填写的中文描述这次没有用上。";
        return new Composed(PromptRules.Compose("", description, fragment), "template", notice, usedDescription ? "description_used" : "description_unused");
    }

    /// <summary>The style with this ID, or null for "auto" or an unknown ID.</summary>
    private static Style FindStyle(string styleId) => string.IsNullOrEmpty(styleId) ? null : Styles().FirstOrDefault(item => item.Id == styleId);

    /// <summary>The fixed English fragments of the product type, the model flag and the style (F53), joined with commas.</summary>
    private static string Fragment(Style style, string productTypeId, bool needModel) =>
        string.Join(", ", new[] { ProductTypeById(productTypeId)?.Prompt, needModel ? ModelFragment : null, style?.Prompt }.Where(part => !string.IsNullOrEmpty(part)));

    /// <summary>Composes the prompt subject when the bilingual prompt panel (F54) may hold text: edited English is sent as is, edited Chinese is translated
    /// to English first (the user's edited text, never an older version), and an empty panel falls back to <see cref="ComposeAsync"/> with the one-line description.
    /// The product-type, model and style fragments are always added. When the translation fails, the template defaults are used with a notice and generation goes on.</summary>
    public static async Task<Composed> ComposePanelAsync(Session session, string styleId, string productTypeId, bool needModel, string description, string promptEn, string promptZh)
    {
        string source = PromptRules.PanelSource(promptEn, promptZh);
        if (source == "")
        {
            return await ComposeAsync(session, styleId, productTypeId, needModel, description, null);
        }
        string fragment = Fragment(FindStyle(styleId), productTypeId, needModel);
        if (source == "en")
        {
            return new Composed(PromptRules.Compose(PromptRules.CleanUserPrompt(promptEn), "", fragment), "panel", null, null);
        }
        string chinese = PromptRules.CleanUserPrompt(promptZh);
        if (PromptRules.IsAscii(chinese))
        {
            return new Composed(PromptRules.Compose(chinese, "", fragment), "panel", null, null);
        }
        string english = await TranslateAsync(session, "English", chinese);
        if (english.Length > 0)
        {
            return new Composed(PromptRules.Compose(english, "", fragment), "translated", null, null, english);
        }
        return new Composed(PromptRules.Compose("", description, fragment), "template",
            "提示词服务暂时不可用，你修改的中文提示词这次没能翻译成英文，已按所选产品类型和风格出图。", "prompt_untranslated");
    }

    /// <summary>"AI expand" (F54): turns the one-line description, or without one the chosen product type, into a fuller English prompt and its Chinese version
    /// for the bilingual panel. Returns null when the language model is unavailable; a failed Chinese translation only leaves the Chinese text empty.</summary>
    public static async Task<Expanded> ExpandAsync(Session session, string styleId, string productTypeId, bool needModel, string description)
    {
        Style style = FindStyle(styleId);
        ProductType productType = ProductTypeById(productTypeId);
        string productName = productType is null || string.IsNullOrEmpty(productType.Prompt) ? null : EcomTemplate.Pick(productType.Name, "en");
        description = description?.Trim() ?? "";
        string text = description.Length > 0 ? description : (productName ?? "a retail product");
        string english = PromptRules.WithoutProduct(await AskModelAsync(session, PromptRules.ExpandInstruction(style is null ? null : EcomTemplate.Pick(style.Name, "en"), productName, needModel), text, null));
        if (english.Length == 0)
        {
            return null;
        }
        string chinese = await TranslateAsync(session, "Simplified Chinese", english);
        return new Expanded(english, chinese);
    }

    /// <summary>Translates a user's prompt text into English (F25/F54); an empty string when the model is unavailable.</summary>
    public static Task<string> TranslateToEnglishAsync(Session session, string text) =>
        TranslateAsync(session, "English", text);

    /// <summary>Translates text into a target language on the vision model when one is set, otherwise the text model (sanitized; empty on any failure).</summary>
    private static async Task<string> TranslateAsync(Session session, string targetLanguage, string text, TimeSpan? limit = null) =>
        PromptRules.Sanitize(await AskModelRawAsync(session, PromptRules.TranslateInstruction(targetLanguage), text, null, limit, true));

    /// <summary>Longest raw model answer kept for line-based parsing.</summary>
    private const int MaxRawAnswerLength = 8000;

    [API.APIDescription("Turn a style and an optional one-line description (Chinese or English) into an English prompt. The answer is prompt text only; when the language model is unavailable the style and template defaults are used and notice explains it. product_image is used only when administrators explicitly allowed product photo description.", "prompt: \"\", source: \"model\", notice: null")]
    public static async Task<JObject> EcomCraftStudioComposePrompt(Session session, string style_id = "", string description = "", string product_image = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        if ((description ?? "").Length > MaxDescriptionLength)
        {
            return StudioApi.Error($"描述最多 {MaxDescriptionLength} 个字符。", "invalid_prompt");
        }
        if (!string.IsNullOrEmpty(product_image) && product_image.Length > 8 * 1024 * 1024)
        {
            return StudioApi.Error("商品图过大。", "invalid_reference");
        }
        Composed composed = await ComposeAsync(session, style_id, "", false, description, product_image);
        return new JObject() { ["prompt"] = composed.Prompt, ["source"] = composed.Source, ["notice"] = composed.Notice, ["notice_code"] = composed.NoticeCode };
    }

    [API.APIDescription("AI expand (F54): turn the one-line description (or, when empty, the chosen product type) into a fuller English prompt plus its Chinese version for the bilingual prompt panel. The answer is prompt text only. prompt_zh is empty when the translation failed.", "prompt_en: \"\", prompt_zh: \"\"")]
    public static async Task<JObject> EcomCraftStudioExpandPrompt(Session session, string description = "", string product_type_id = "", string style_id = "", bool need_model = false)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        if ((description ?? "").Length > MaxDescriptionLength)
        {
            return StudioApi.Error($"描述最多 {MaxDescriptionLength} 个字符。", "invalid_prompt");
        }
        if (!KnownProductType(product_type_id))
        {
            return StudioApi.Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
        }
        long started = Environment.TickCount64;
        Expanded expanded = await ExpandAsync(session, style_id, product_type_id, need_model, description);
        if (expanded is null)
        {
            return StudioApi.Error("提示词服务暂时不可用，可直接用一句话出图。", "prompt_service_unavailable");
        }
        Generation.TaskTiming.RecordSince(Generation.TaskTiming.Expand, started);
        return new JObject() { ["prompt_en"] = expanded.English, ["prompt_zh"] = expanded.Chinese };
    }
}
