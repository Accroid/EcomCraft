using System.IO;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Prompting;

/// <summary>Prompt service (F37): a style plus an optional one-line description (Chinese or English) becomes an English prompt through MagicPrompt.
/// The model's answer only ever becomes prompt text: it cannot choose a workflow, parameter or execution server. When the model is unavailable or slow,
/// the template and style defaults are used with a plain notice. Sending product photos to a vision model is off by default and only administrators can turn it on.</summary>
[API.APIClass("EcomCraft prompt service: styles and one-line description to English prompt.")]
public static class PromptService
{
    /// <summary>Longest wait for the language model before falling back.</summary>
    private static readonly TimeSpan ModelTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Longest description accepted.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>One visual style users can pick: names, description and the English prompt fragment it adds.</summary>
    public sealed record class Style(string Id, Dictionary<string, string> Name, Dictionary<string, string> Description, string Prompt);

    /// <summary>One product subtype users can pick (F53): names and the English prompt fragment it adds.</summary>
    public sealed record class ProductType(string Id, Dictionary<string, string> Name, string Prompt);

    /// <summary>One product-type category (F53) holding its subtypes.</summary>
    public sealed record class ProductCategory(string Id, Dictionary<string, string> Name, List<ProductType> Subtypes);

    /// <summary>The fixed prompt fragment added when "needs a model" is checked (F53); gender or hand-model choices may extend it later.</summary>
    public const string ModelFragment = "an elegant model presenting the product, natural pose, shallow depth of field";

    /// <summary>The result of composing: the prompt subject, where it came from ("model", "template"), and when the model was not used a plain notice
    /// with a code the client can translate ("description_used" when the English description was used as is, "description_unused" otherwise).</summary>
    public sealed record class Composed(string Prompt, string Source, string Notice, string NoticeCode);

    /// <summary>Folder holding styles.json, set at startup.</summary>
    private static string TemplatesDir;

    /// <summary>File of the administrator-controlled prompt settings.</summary>
    private static string SettingsFile => Path.Combine(Program.DataDir, "EcomCraft", "prompting.json");

    /// <summary>Protects the settings file.</summary>
    private static readonly object SettingsLock = new();

    /// <summary>Sets the extension folder that holds Templates/styles.json.</summary>
    public static void Init(string extensionFolder)
    {
        TemplatesDir = Path.Combine(extensionFolder, "Templates");
    }

    /// <summary>The built-in styles; invalid entries are skipped and logged.</summary>
    public static List<Style> Styles()
    {
        List<Style> styles = [];
        try
        {
            string file = Path.Combine(TemplatesDir ?? "", "styles.json");
            if (!File.Exists(file))
            {
                return styles;
            }
            foreach (JObject item in (JObject.Parse(File.ReadAllText(file))["styles"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string id = item["id"]?.ToString() ?? "";
                string prompt = item["prompt"]?.ToString() ?? "";
                Dictionary<string, string> name = item["name"]?.ToObject<Dictionary<string, string>>() ?? [];
                if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$") || !name.ContainsKey("zh-CN") || !name.ContainsKey("en")
                    || prompt.Length > 300 || PromptRules.Sanitize(prompt) != prompt)
                {
                    Logs.Warning($"EcomCraft skipped an invalid style '{id}'.");
                    continue;
                }
                styles.Add(new Style(id, name, item["description"]?.ToObject<Dictionary<string, string>>() ?? [], prompt));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the style list: {ex.Message}");
        }
        return styles;
    }

    /// <summary>The built-in fallback when product-types.json is unreadable: only the general category, so the step keeps working (F53).</summary>
    private static List<ProductCategory> FallbackProductTypes() =>
    [
        new ProductCategory("general", new Dictionary<string, string>() { ["zh-CN"] = "通用商品", ["en"] = "General product" },
            [new ProductType("unspecified", new Dictionary<string, string>() { ["zh-CN"] = "不指定", ["en"] = "Unspecified" }, "")])
    ];

    /// <summary>The product-type categories from Templates/product-types.json (F53). Invalid entries are skipped and logged;
    /// when the file is missing or unreadable the built-in general category keeps the step usable.</summary>
    public static List<ProductCategory> ProductTypes()
    {
        List<ProductCategory> categories = [];
        try
        {
            string file = Path.Combine(TemplatesDir ?? "", "product-types.json");
            if (!File.Exists(file))
            {
                return FallbackProductTypes();
            }
            foreach (JObject item in (JObject.Parse(File.ReadAllText(file))["categories"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string id = item["id"]?.ToString() ?? "";
                Dictionary<string, string> name = item["name"]?.ToObject<Dictionary<string, string>>() ?? [];
                if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$") || !name.ContainsKey("zh-CN") || !name.ContainsKey("en"))
                {
                    Logs.Warning($"EcomCraft skipped an invalid product-type category '{id}'.");
                    continue;
                }
                List<ProductType> subtypes = [];
                foreach (JObject subtype in (item["subtypes"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    ProductType type = ParseProductSubtype(subtype);
                    if (type is null || subtypes.Any(existing => existing.Id == type.Id) || categories.SelectMany(category => category.Subtypes).Any(existing => existing.Id == type.Id))
                    {
                        continue;
                    }
                    subtypes.Add(type);
                }
                if (subtypes.Count == 0)
                {
                    Logs.Warning($"EcomCraft skipped the product-type category '{id}' because it has no valid subtypes.");
                    continue;
                }
                categories.Add(new ProductCategory(id, name, subtypes));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the product-type list: {ex.Message}");
        }
        return categories.Count > 0 ? categories : FallbackProductTypes();
    }

    /// <summary>Reads one subtype; null with a logged warning when it is not valid.</summary>
    private static ProductType ParseProductSubtype(JObject item)
    {
        string id = item["id"]?.ToString() ?? "";
        Dictionary<string, string> name = item["name"]?.ToObject<Dictionary<string, string>>() ?? [];
        string prompt = item["prompt"]?.ToString() ?? "";
        if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$") || !name.ContainsKey("zh-CN") || !name.ContainsKey("en")
            || prompt.Length > 300 || PromptRules.Sanitize(prompt) != prompt)
        {
            Logs.Warning($"EcomCraft skipped an invalid product subtype '{id}'.");
            return null;
        }
        return new ProductType(id, name, prompt);
    }

    /// <summary>The subtype with this ID across all categories, or null. An empty ID means "unspecified".</summary>
    public static ProductType ProductTypeById(string id) => string.IsNullOrWhiteSpace(id)
        ? null : ProductTypes().SelectMany(category => category.Subtypes).FirstOrDefault(type => type.Id == id);

    /// <summary>Whether a product type ID is known (or empty, meaning unspecified), so client-submitted IDs stay whitelisted (F53).</summary>
    public static bool KnownProductType(string id) => string.IsNullOrWhiteSpace(id) || ProductTypeById(id) is not null;

    /// <summary>Whether administrators allowed sending product photos to the vision model.</summary>
    public static bool VisionEnabled()
    {
        lock (SettingsLock)
        {
            try
            {
                return File.Exists(SettingsFile) && JObject.Parse(File.ReadAllText(SettingsFile)).Value<bool?>("vision_enabled") == true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>Composes the prompt subject for a product type, an optional style, the optional model flag and a description (F37/F53).
    /// The template's own prefix and suffix are added by the template request.
    /// <paramref name="productImage"/> (a data URL) is used only when administrators enabled vision.</summary>
    public static async Task<Composed> ComposeAsync(Session session, string styleId, string productTypeId, bool needModel, string description, string productImage)
    {
        Style style = string.IsNullOrEmpty(styleId) ? null : Styles().FirstOrDefault(item => item.Id == styleId);
        ProductType productType = ProductTypeById(productTypeId);
        string fragment = string.Join(", ", new[] { productType?.Prompt, needModel ? ModelFragment : null, style?.Prompt }
            .Where(part => !string.IsNullOrEmpty(part)));
        description = description?.Trim() ?? "";
        string productHint = "";
        if (!string.IsNullOrEmpty(productImage) && VisionEnabled())
        {
            productHint = await AskModelAsync(session, "Describe the main product in this photo in one short English phrase (type, color, material). Reply with the phrase only.",
                "Describe the product.", productImage);
        }
        if (description.Length == 0 && productHint.Length == 0)
        {
            return new Composed(PromptRules.Compose("", "", fragment), "template", null, null);
        }
        string text = string.Join(". ", new[] { productHint, description }.Where(part => part.Length > 0));
        string answer = await AskModelAsync(session, PromptRules.Instruction(style is null ? null : EcomTemplate.Pick(style.Name, "en")), text, null);
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

    /// <summary>Asks the configured MagicPrompt model; returns the sanitized answer, or an empty string on any failure or timeout. Errors are logged without the text.</summary>
    private static async Task<string> AskModelAsync(Session session, string instructions, string text, string imageDataUrl)
    {
        try
        {
            JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
            bool vision = imageDataUrl is not null;
            string model = settings?[vision ? "visionmodel" : "model"]?.ToString();
            if (string.IsNullOrWhiteSpace(model))
            {
                return "";
            }
            JObject content = new() { ["text"] = text, ["instructions"] = instructions };
            if (vision)
            {
                int comma = imageDataUrl.IndexOf(',');
                if (comma < 0 || !imageDataUrl.StartsWith("data:image/", StringComparison.Ordinal))
                {
                    return "";
                }
                content["media"] = new JArray(new JObject()
                {
                    ["Type"] = "base64",
                    ["Data"] = imageDataUrl[(comma + 1)..],
                    ["MediaType"] = imageDataUrl[5..comma].Replace(";base64", "")
                });
            }
            JObject request = new() { ["messageContent"] = content, ["modelId"] = model, ["messageType"] = vision ? "Vision" : "Text", ["action"] = vision ? "vision" : "chat" };
            Task<JObject> call = LLMAPICalls.MagicPromptPhoneHome(request, session);
            if (await Task.WhenAny(call, Task.Delay(ModelTimeout)) != call)
            {
                Logs.Warning("EcomCraft prompt service: the language model did not answer in time; using the template defaults.");
                return "";
            }
            JObject result = await call;
            if (result?.Value<bool?>("success") != true)
            {
                Logs.Warning("EcomCraft prompt service: the language model returned an error; using the template defaults.");
                return "";
            }
            return PromptRules.Sanitize(result["response"]?.ToString());
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft prompt service failed: {ex.GetType().Name}");
            return "";
        }
    }

    [API.APIDescription("List the visual styles users can pick, with names and descriptions in the requested language (zh-CN or en).", "styles: [{ id: \"studio-clean\", name: \"\", description: \"\" }]")]
    public static Task<JObject> EcomCraftStudioStyles(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        return Task.FromResult(new JObject()
        {
            ["styles"] = new JArray(Styles().Select(style => new JObject()
            {
                ["id"] = style.Id,
                ["name"] = EcomTemplate.Pick(style.Name, language),
                ["description"] = EcomTemplate.Pick(style.Description, language)
            })),
            ["vision_enabled"] = VisionEnabled()
        });
    }

    [API.APIDescription("List the product-type categories and subtypes users can pick (F53), with names in the requested language (zh-CN or en). The English prompt fragments are not exposed.", "categories: [{ id: \"care\", name: \"\", subtypes: [{ id: \"shampoo\", name: \"\" }] }]")]
    public static Task<JObject> EcomCraftStudioProductTypes(Session session, string language = "zh-CN")
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        language = language == "en" ? "en" : "zh-CN";
        return Task.FromResult(new JObject()
        {
            ["categories"] = new JArray(ProductTypes().Select(category => new JObject()
            {
                ["id"] = category.Id,
                ["name"] = EcomTemplate.Pick(category.Name, language),
                ["subtypes"] = new JArray(category.Subtypes.Select(subtype => new JObject()
                {
                    ["id"] = subtype.Id,
                    ["name"] = EcomTemplate.Pick(subtype.Name, language)
                }))
            }))
        });
    }

    [API.APIDescription("Turn a style and an optional one-line description (Chinese or English) into an English prompt. The answer is prompt text only; when the language model is unavailable the style and template defaults are used and notice explains it. product_image is used only when administrators enabled vision.", "prompt: \"\", source: \"model\", notice: null")]
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

    [API.APIDescription("Read or change whether product photos may be sent to the configured vision model to describe the product (off by default). Returns the provider photos would be sent to. Requires the server-settings permission.", "vision_enabled: false, vision_provider: \"ollama\"")]
    public static async Task<JObject> EcomCraftPromptSettings(Session session, string vision_enabled = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("此账号没有管理提示词服务的权限。", "bad_permissions");
        }
        if (bool.TryParse(vision_enabled, out bool enable))
        {
            lock (SettingsLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                    string temp = $"{SettingsFile}.tmp";
                    File.WriteAllText(temp, new JObject() { ["vision_enabled"] = enable, ["changed_by"] = session.User.UserID }.ToString(Formatting.Indented));
                    File.Move(temp, SettingsFile, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logs.Error($"EcomCraft could not save the prompt settings: {ex.Message}");
                    return StudioApi.Error("提示词设置无法保存到服务器数据目录。", "prompt_settings_failed");
                }
            }
        }
        JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
        return new JObject()
        {
            ["vision_enabled"] = VisionEnabled(),
            ["vision_provider"] = settings?["visionbackend"]?.ToString() ?? settings?["backend"]?.ToString() ?? "",
            ["vision_model"] = settings?["visionmodel"]?.ToString() ?? ""
        };
    }
}
