using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Products;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Model-suggested selling points (F79, T99). Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
    /// <summary>Whether a text prompt model is configured: the page only offers the "suggest selling points" button when there is one.</summary>
    public static async Task<bool> PromptModelConfigured()
    {
        JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
        return !string.IsNullOrWhiteSpace(settings?["model"]?.ToString());
    }

    [API.APIDescription("Suggest 3 to 5 selling points from the user's own product description (F79, T99), in the asked language (\"zh-CN\" or \"en\"). The model is told to summarize only what the text says and never add parameters, certifications, effects, sales figures or rankings; the suggestions are shown for the user to tick and confirm — nothing is saved here. Every suggestion is checked against the advertising-claim word list and carries its hits. Requires a configured prompt model.", "suggestions: [{ text: \"…\", claims: [{ word: \"最佳\" }] }]")]
    public static async Task<JObject> EcomCraftStudioProductSuggestPoints(Session session, string description, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        description = description?.Trim() ?? "";
        if (description.Length == 0 || description.Length > MaxDescriptionLength)
        {
            return StudioApi.Error($"商品描述须为 1–{MaxDescriptionLength} 个字符。", "invalid_prompt");
        }
        string languageId = ProductInfoRules.LanguageId(language) ?? "zh-CN";
        long started = Environment.TickCount64;
        // The raw answer, not the sanitized one: PromptRules.Sanitize collapses model output onto one line, but the suggestions are parsed per line.
        string answer = await AskModelRawAsync(session, ProductInfoRules.SuggestInstruction(languageId == "en" ? "English" : "Simplified Chinese"), description, null);
        List<string> points = ProductInfoRules.ParseSuggestedPoints(answer);
        if (points.Count == 0)
        {
            return StudioApi.Error("提示词服务暂时不可用，请自己填写卖点。", "prompt_service_unavailable");
        }
        Generation.TaskTiming.RecordSince(Generation.TaskTiming.Expand, started);
        // The suggestions join the page as drafts the user confirms; they go through the same wording check as hand-written text.
        List<ClaimRules.ClaimWord> words = ClaimWords.Words();
        return new JObject()
        {
            ["suggestions"] = new JArray(points.Select(point => new JObject()
            {
                ["text"] = point,
                ["claims"] = ClaimWords.HitsJson(ClaimRules.Check(point, words))
            }))
        };
    }
}
