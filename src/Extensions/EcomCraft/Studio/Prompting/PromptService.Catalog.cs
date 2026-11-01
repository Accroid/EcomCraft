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


/// <summary>The style and product-type catalog and its API routes. Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
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
}
