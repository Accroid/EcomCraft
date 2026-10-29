using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.MagicPrompt.WebAPI.Models;
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

/// <summary>Prompt service (F37): a style plus an optional one-line description (Chinese or English) becomes an English prompt through MagicPrompt.
/// The model's answer only ever becomes prompt text: it cannot choose a workflow, parameter or execution server. When the model is unavailable or slow,
/// the template and style defaults are used with a plain notice. Describing reference images is on by default; sending product photos to a vision model is off by default and only administrators can turn it on (A5).</summary>
[API.APIClass("EcomCraft prompt service: styles and one-line description to English prompt.")]
public static class PromptService
{
    /// <summary>Longest wait for the language model before falling back.</summary>
    private static readonly TimeSpan ModelTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Longest wait for the vision model: a local one may first have to load into memory, reading an image is slower than text,
    /// and a first Ollama request that hangs on the GPU is retried on the CPU.</summary>
    private static readonly TimeSpan VisionTimeout = TimeSpan.FromSeconds(180);

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

    /// <summary>The result of composing: the prompt subject, where it came from ("model", "template", "panel", "translated"), and when the model was not used a plain notice
    /// with a code the client can translate ("description_used" when the English description was used as is, "description_unused" otherwise,
    /// "prompt_untranslated" when the edited Chinese panel text could not be translated). Translated is the English translation of
    /// the Chinese panel text (F54), so the client can show it on the English tab.</summary>
    public sealed record class Composed(string Prompt, string Source, string Notice, string NoticeCode, string Translated = null);

    /// <summary>The result of "AI expand" (F54): the English prompt text, and its Chinese version for the panel (empty when the translation failed).</summary>
    public sealed record class Expanded(string English, string Chinese);

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

    /// <summary>The administrator's prompt settings file, or null when there is none or it cannot be read.</summary>
    private static JObject ReadPromptSettings()
    {
        lock (SettingsLock)
        {
            try
            {
                return File.Exists(SettingsFile) ? JObject.Parse(File.ReadAllText(SettingsFile)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Whether reference images may be described by the vision model (A5, F68): on unless an administrator turned it off.
    /// An unreadable settings file counts as off, so a damaged file never sends images.</summary>
    public static bool VisionEnabled()
    {
        lock (SettingsLock)
        {
            JObject settings = ReadPromptSettings();
            return (settings is not null || !File.Exists(SettingsFile)) && PromptRules.VisionOn(settings);
        }
    }

    /// <summary>Whether administrators explicitly allowed sending product photos to the vision model (A5); off by default.</summary>
    public static bool ProductVisionEnabled() => PromptRules.ProductVisionOn(ReadPromptSettings());

    /// <summary>How long a vision status answer is reused, so opening the page does not probe the vision service every time.</summary>
    private static readonly TimeSpan VisionStatusTtl = TimeSpan.FromSeconds(15);

    /// <summary>The last vision status (state, provider, model) and when it was checked.</summary>
    private static (DateTime Checked, string State, string Provider, string Model) LastVisionStatus;

    /// <summary>Protects <see cref="LastVisionStatus"/>.</summary>
    private static readonly object VisionStatusLock = new();

    /// <summary>Checks whether image description can work (F68): the switch, the configured vision model and whether the vision service lists it.</summary>
    private static async Task<(string State, string Provider, string Model)> CheckVisionAsync(Session session)
    {
        lock (VisionStatusLock)
        {
            if (DateTime.UtcNow - LastVisionStatus.Checked < VisionStatusTtl)
            {
                return (LastVisionStatus.State, LastVisionStatus.Provider, LastVisionStatus.Model);
            }
        }
        string state, provider = "", model = "";
        try
        {
            JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
            provider = (settings?["visionbackend"]?.ToString() ?? settings?["backend"]?.ToString() ?? "").ToLowerFast();
            model = settings?["visionmodel"]?.ToString() ?? "";
            List<string> available = null;
            // The vision service may have its own address and key (F68).
            JObject visionSettings = settings is null ? null : LLMAPICalls.VisionSettings(settings);
            if (VisionEnabled() && !string.IsNullOrWhiteSpace(model) && visionSettings is not null && await LLMAPICalls.IsServerReachable(provider, visionSettings))
            {
                Task<List<ModelData>> listing = LLMAPICalls.GetModelsForBackend(provider, visionSettings, true, session, LLMAPICalls.VisionKeyOverride(settings, session));
                if (await Task.WhenAny(listing, Task.Delay(TimeSpan.FromSeconds(10))) == listing)
                {
                    List<ModelData> models = await listing ?? [];
                    // A local service that answers with no models has none installed; an empty answer from a cloud service means it could not be used (address or key).
                    if (models.Count > 0 || provider is "ollama" or "openaiapi")
                    {
                        available = [.. models.SelectMany(item => new[] { item.Model, item.Name, item.Id })];
                    }
                }
            }
            state = PromptRules.VisionState(VisionEnabled(), model, available);
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft could not check the vision model: {ex.GetType().Name}");
            state = VisionEnabled() ? "unreachable" : "off";
        }
        lock (VisionStatusLock)
        {
            LastVisionStatus = (DateTime.UtcNow, state, provider, model);
        }
        return (state, provider, model);
    }

    /// <summary>Forgets the cached vision status, after the administrator changed the switch or an install finished.</summary>
    public static void ForgetVisionStatus()
    {
        lock (VisionStatusLock)
        {
            LastVisionStatus = default;
        }
    }

    [API.APIDescription("Whether image description for reference images (F59) can work now (F68), so the page can explain what is missing. "
        + "state is ready, off, no_model, unreachable or model_missing; provider and model name the configured vision service type and model (never its address or key); "
        + "can_manage tells whether this account may change the settings.", "state: \"ready\", provider: \"ollama\", model: \"llama3.2-vision:latest\", can_manage: false")]
    public static async Task<JObject> EcomCraftStudioVisionStatus(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return StudioApi.Error("无权访问创作工作台。", "bad_permissions");
        }
        (string state, string provider, string model) = await CheckVisionAsync(session);
        return new JObject()
        {
            ["state"] = state,
            ["provider"] = provider,
            ["model"] = model,
            ["can_manage"] = session.User.HasPermission(Permissions.EditServerSettings)
        };
    }

    /// <summary>Longest wait for listing the vision service's models when switching the vision model.</summary>
    private static readonly TimeSpan VisionListTimeout = TimeSpan.FromSeconds(25);

    /// <summary>The models the configured vision service offers, or null when it cannot be reached or listed in time.</summary>
    private static async Task<List<string>> ListVisionModelsAsync(Session session)
    {
        using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(Program.GlobalProgramCancel);
        Task<List<string>> listing = ModelPreviewAPI.SavedVisionModels(session, cancel.Token);
        try
        {
            if (await Task.WhenAny(listing, Task.Delay(VisionListTimeout)) != listing)
            {
                cancel.Cancel();
                return null;
            }
            return await listing;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or ObjectDisposedException)
        {
            return null;
        }
    }

    [API.APIDescription("List the vision models the configured vision service offers (administrators, F68), so the reference page can switch the vision model "
        + "without opening the settings. Only model names and the service type; never the service address or key.",
        "models: [\"qwen3-vl:2b\", \"qwen3-vl:4b\"], model: \"qwen3-vl:4b\", provider: \"ollama\"")]
    public static async Task<JObject> EcomCraftStudioVisionModels(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("只有管理员可以切换看图模型。", "bad_permissions");
        }
        List<string> models = await ListVisionModelsAsync(session);
        if (models is null)
        {
            return StudioApi.Error("连不上视觉服务，暂时无法列出看图模型，请确认服务已启动。", "vision_unreachable");
        }
        JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
        return new JObject()
        {
            ["models"] = new JArray(models),
            ["model"] = settings?["visionmodel"]?.ToString() ?? "",
            ["provider"] = settings is null ? "" : LLMAPICalls.VisionBackend(settings)
        };
    }

    [API.APIDescription("Switch the vision model used to describe reference images (administrators, F68): the same setting as in the prompt enhancement settings. "
        + "The model must be one the configured vision service lists.", "success: true, model: \"qwen3-vl:2b\"")]
    public static async Task<JObject> EcomCraftStudioSetVisionModel(Session session, string model = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("只有管理员可以切换看图模型。", "bad_permissions");
        }
        model = model?.Trim() ?? "";
        if (model.Length == 0 || model.Length > 200)
        {
            return StudioApi.Error("请选择一个看图模型。", "invalid_vision_model");
        }
        List<string> models = await ListVisionModelsAsync(session);
        if (models is null)
        {
            return StudioApi.Error("连不上视觉服务，暂时无法切换看图模型，请确认服务已启动。", "vision_unreachable");
        }
        if (!models.Contains(model))
        {
            return StudioApi.Error("视觉服务里没有这个模型，请重新选择。", "invalid_vision_model");
        }
        JObject result = await SessionSettings.SaveMagicPromptSettings(new JObject() { ["settings"] = new JObject() { ["visionmodel"] = model } });
        if (result?.Value<bool?>("success") != true)
        {
            return StudioApi.Error("看图模型保存失败，请稍后重试。", "prompt_settings_failed");
        }
        ForgetVisionStatus();
        Logs.Info($"EcomCraft vision model switched to {model}.");
        return new JObject() { ["success"] = true, ["model"] = model };
    }

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
        string english = await AskModelAsync(session, PromptRules.TranslateInstruction("English"), chinese, null);
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
        string english = await AskModelAsync(session, PromptRules.ExpandInstruction(style is null ? null : EcomTemplate.Pick(style.Name, "en"), productName, needModel), text, null);
        if (english.Length == 0)
        {
            return null;
        }
        string chinese = await AskModelAsync(session, PromptRules.TranslateInstruction("Simplified Chinese"), english, null);
        return new Expanded(english, chinese);
    }

    /// <summary>Asks the configured MagicPrompt model; returns the sanitized answer, or an empty string on any failure or timeout. Errors are logged without the text.</summary>
    private static async Task<string> AskModelAsync(Session session, string instructions, string text, string imageDataUrl) =>
        PromptRules.Sanitize(await AskModelRawAsync(session, instructions, text, imageDataUrl));

    /// <summary>What a reference image may be at most, as a data URL — the same cap as product photos.</summary>
    private const int MaxReferenceImageLength = (5 * 1024 * 1024 + 2) / 3 * 4 + 40;

    /// <summary>The instruction for describing one reference image (F59): the setup and style only, never the product, so the description can steer a
    /// different product into a similar frame without copying it. It asks for a detailed paragraph, since a one-sentence answer gives the image model too little to go on.</summary>
    private const string ReferenceInstruction = "You write prompts for a text-to-image model. Describe how this product photo is set up so a similar photo can be made "
        + "with a different product. Cover: shot type and camera angle; where the product sits in the frame and how much of it it fills; the surface and background "
        + "(materials, colors, textures); props and where they are placed; lighting (direction, softness, shadows, highlights); color palette and mood; "
        + "the overall photographic style. Call the product only \"the product\" and never name its type, brand, logo or any text in the photo. "
        + "Reply with one English paragraph of 60 to 120 words, without lists or headings.";

    [API.APIDescription("Describe reference images for reference mode (F59): one English composition prompt and its Chinese translation per image (1–8), "
        + "for the page to show and the user to edit. Image description is on unless the administrator turned it off.", "items: [{ index: 0, prompt_en: \"\", prompt_zh: \"\" }], failed: 0")]
    public static async Task<JObject> EcomCraftStudioReferenceDescribe(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return StudioApi.Error("此账号没有生成权限。", "bad_permissions");
        }
        if (!VisionEnabled())
        {
            return StudioApi.Error("看图描述已被管理员关闭，无法描述参考图。", "vision_disabled");
        }
        if (rawInput["reference_images"] is not JArray images || images.Count == 0 || images.Count > Generation.SetRules.MaxReferences)
        {
            return StudioApi.Error($"参考图须为 1–{Generation.SetRules.MaxReferences} 张。", "invalid_reference");
        }
        foreach (JToken image in images)
        {
            string data = image?.Type == JTokenType.String ? image.ToString() : "";
            if (!data.StartsWith("data:image/", StringComparison.Ordinal) || data.Length > MaxReferenceImageLength)
            {
                return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
            }
        }
        // One description per image, in parallel; a failed one comes back empty so the page can retry it alone.
        (string english, string chinese)[] answers = await Task.WhenAll(images.Select(async (image, index) =>
        {
            long started = Environment.TickCount64;
            // A short user text keeps the request valid for every backend (some reject image-only or empty-text messages).
            string described = await AskModelAsync(session, ReferenceInstruction, "Describe the setup of this product photo.", image.ToString());
            if (described.Length == 0)
            {
                return ("", "");
            }
            string translated = await AskModelAsync(session, PromptRules.TranslateInstruction("Simplified Chinese"), described, null);
            // How long one reference takes, for the waiting view (F70); failed descriptions are not counted.
            Generation.TaskTiming.RecordSince(Generation.TaskTiming.Describe, started);
            return (described, translated);
        }));
        JArray items = new();
        int failed = 0;
        for (int index = 0; index < answers.Length; index++)
        {
            if (answers[index].english.Length == 0)
            {
                failed++;
            }
            items.Add(new JObject() { ["index"] = index, ["prompt_en"] = answers[index].english, ["prompt_zh"] = answers[index].chinese });
        }
        return new JObject() { ["items"] = items, ["failed"] = failed };
    }

    /// <summary>Translates a user's prompt text into English (F25/F54); an empty string when the model is unavailable.</summary>
    public static Task<string> TranslateToEnglishAsync(Session session, string text) =>
        AskModelAsync(session, PromptRules.TranslateInstruction("English"), text, null);

    /// <summary>Longest raw model answer kept for line-based parsing.</summary>
    private const int MaxRawAnswerLength = 8000;

    /// <summary>Asks the configured MagicPrompt model and returns its raw answer (at most <see cref="MaxRawAnswerLength"/> characters) for callers that parse
    /// a line format themselves, or an empty string on any failure or timeout. Callers must still clean everything they keep.</summary>
    private static async Task<string> AskModelRawAsync(Session session, string instructions, string text, string imageDataUrl)
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
                // Names the model actually asked, so administrators can confirm a switch took effect; never the address, key or image.
                Logs.Info($"EcomCraft is describing an image with {LLMAPICalls.VisionBackend(settings)} / {model}.");
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
            TimeSpan limit = vision ? VisionTimeout : ModelTimeout;
            // The limit also goes to the call, so a request given up here does not keep the model busy upstream.
            JObject request = new() { ["messageContent"] = content, ["modelId"] = model, ["messageType"] = vision ? "Vision" : "Text", ["action"] = vision ? "vision" : "chat",
                ["timeout"] = (int)limit.TotalSeconds };
            Task<JObject> call = LLMAPICalls.MagicPromptPhoneHome(request, session);
            // A little extra over the call's own limit leaves time to unload a hung Ollama model.
            if (await Task.WhenAny(call, Task.Delay(limit + TimeSpan.FromSeconds(20))) != call)
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
            string answer = result["response"]?.ToString() ?? "";
            return answer.Length > MaxRawAnswerLength ? answer[..MaxRawAnswerLength] : answer;
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
        List<StoryboardRules.Shot> shots = parsed.Shots;
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

    [API.APIDescription("Read or change whether reference images may be described by the configured vision model (on by default) and whether product photos may be sent to it "
        + "to describe the product (off by default). Returns the provider images would be sent to, the local vision device choice and whether it was moved to the CPU after the GPU hung. Requires the server-settings permission.",
        "vision_enabled: true, product_vision_enabled: false, vision_provider: \"ollama\", vision_device: \"auto\", vision_moved_to_cpu: false")]
    public static async Task<JObject> EcomCraftPromptSettings(Session session, string vision_enabled = "", string product_vision_enabled = "")
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("此账号没有管理提示词服务的权限。", "bad_permissions");
        }
        bool changeVision = bool.TryParse(vision_enabled, out bool enable);
        bool changeProduct = bool.TryParse(product_vision_enabled, out bool enableProduct);
        if (changeVision || changeProduct)
        {
            lock (SettingsLock)
            {
                try
                {
                    // Keep the other switch: an unreadable file is replaced only with the defaults plus this change.
                    JObject saved = ReadPromptSettings() ?? new JObject();
                    if (changeVision)
                    {
                        saved["vision_enabled"] = enable;
                    }
                    if (changeProduct)
                    {
                        saved["product_vision_enabled"] = enableProduct;
                    }
                    saved["changed_by"] = session.User.UserID;
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                    string temp = $"{SettingsFile}.tmp";
                    File.WriteAllText(temp, saved.ToString(Formatting.Indented));
                    File.Move(temp, SettingsFile, true);
                    ForgetVisionStatus();
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
            ["product_vision_enabled"] = ProductVisionEnabled(),
            ["vision_provider"] = settings?["visionbackend"]?.ToString() ?? settings?["backend"]?.ToString() ?? "",
            ["vision_model"] = settings?["visionmodel"]?.ToString() ?? "",
            // The device choice for a local Ollama vision model (F71), and whether "auto" moved it to the CPU because the GPU hung; never the address.
            ["vision_device"] = VisionDeviceRules.Normalize(settings?["visiondevice"]?.ToString()),
            ["vision_moved_to_cpu"] = LLMAPICalls.VisionMovedToCpu(settings)
        };
    }
}
