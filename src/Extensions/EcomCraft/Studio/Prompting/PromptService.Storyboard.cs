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


/// <summary>The storyboard generator (F25). Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
    [API.APIDescription("Selling points and set storyboard (F25): from the description or prompt panel text, optional selling points (one per line), product type, style and model flag, the language model lists selling points and plans up to 8 images, each with a template of the platform, an English prompt fragment and its purpose. Every shot is checked against the platform's runnable templates and its fragment is cleaned; when the model is unavailable or gives nothing usable, one shot per set template is returned with source \"fallback\".", "selling_points: [], shots: [{ template_id: \"\", template_name: \"\", prompt: \"\", purpose: \"\" }], source: \"model\", notice_code: null")]
    public static async Task<JObject> EcomCraftStudioStoryboard(Session session, string description = "", string selling_points = "", string product_type_id = "", string style_id = "",
        bool need_model = false, string platform = "", string language = "zh-CN", string prompt_en = "", string prompt_zh = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration) || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        description = description?.Trim() ?? "";
        if (description.Length > MaxDescriptionLength || (selling_points ?? "").Length > 1000
            || (prompt_en ?? "").Length > PromptRules.MaxPromptLength || (prompt_zh ?? "").Length > PromptRules.MaxPromptLength)
        {
            return StudioApi.Error("描述、卖点或提示词太长，请精简后重试。", "invalid_prompt");
        }
        if (!KnownProductType(product_type_id))
        {
            return StudioApi.Error("未知的产品类型，请刷新页面后重选。", "invalid_product_type");
        }
        language = language == "en" ? "en" : "zh-CN";
        platform ??= "";
        List<string> userPoints = StoryboardRules.CleanPoints((selling_points ?? "").Split('\n'));
        List<EcomTemplate> suitable = [.. TemplateApi.RunnableTemplates(session).Select(item => item.Template)
            .Where(template => SetRules.SuitsPlatform(new SetRules.TemplateRef(template.Id, template.Platforms, template.Sets), platform))];
        if (suitable.Count == 0)
        {
            return StudioApi.Error("这个平台暂时没有可用的图片类型。请管理员发布模板，或确认执行服务器在线。", "invalid_template");
        }
        HashSet<string> allowed = [.. suitable.Select(template => template.Id)];
        Style style = FindStyle(style_id);
        ProductType productType = ProductTypeById(product_type_id);
        string productName = productType is null || string.IsNullOrEmpty(productType.Prompt) ? null : EcomTemplate.Pick(productType.Name, "en");
        string subject = new[] { PromptRules.CleanUserPrompt(prompt_en), PromptRules.CleanUserPrompt(prompt_zh), PromptRules.CleanUserPrompt(description) }
            .FirstOrDefault(part => part.Length > 0) ?? productName ?? "a retail product";
        string text = userPoints.Count > 0 ? $"{subject}\nSelling points: {string.Join("; ", userPoints)}" : subject;
        long started = Environment.TickCount64;
        string answer = await AskModelRawAsync(session, StoryboardRules.Instruction(language == "en" ? "English" : "Simplified Chinese",
            suitable.Select(template => (template.Id, EcomTemplate.Pick(template.Name, "en"))), productName, style is null ? null : EcomTemplate.Pick(style.Name, "en"), need_model), text, null);
        StoryboardRules.Storyboard parsed = StoryboardRules.Parse(answer, allowed);
        if (parsed.Shots.Count > 0)
        {
            // How long a storyboard takes, for the waiting view (F70); a fallback (no usable answer) is not counted.
            Generation.TaskTiming.RecordSince(Generation.TaskTiming.Storyboard, started);
        }
        List<string> points = StoryboardRules.CleanPoints(userPoints.Concat(parsed.Points));
        // Shot fragments are model-written like the composed prompt, so what they say about the product is removed too; a shot left empty keeps only the batch prompt.
        List<StoryboardRules.Shot> shots = [.. parsed.Shots.Select(shot => shot with { Prompt = PromptRules.WithoutProduct(shot.Prompt) })];
        string source = "model";
        if (shots.Count == 0)
        {
            // Nothing usable from the model: one shot per template of the platform's standard set (or every suitable template), to be edited by hand.
            source = "fallback";
            List<SetRules.TemplateRef> set = SetRules.SetTemplates(suitable.Select(template => new SetRules.TemplateRef(template.Id, template.Platforms, template.Sets)), platform, "standard");
            IEnumerable<EcomTemplate> fallback = set.Count > 0 ? set.Select(item => suitable.First(template => template.Id == item.Id)) : suitable;
            shots = StoryboardRules.Fallback(fallback.Select(template => (template.Id, EcomTemplate.Pick(template.Name, language))));
        }
        return new JObject()
        {
            ["selling_points"] = new JArray(points),
            ["shots"] = new JArray(shots.Select(shot => new JObject()
            {
                ["template_id"] = shot.TemplateId,
                ["template_name"] = EcomTemplate.Pick(suitable.First(template => template.Id == shot.TemplateId).Name, language),
                ["prompt"] = shot.Prompt,
                ["purpose"] = shot.Purpose
            })),
            ["source"] = source,
            ["notice_code"] = source == "fallback" ? "storyboard_fallback" : null
        };
    }
}
