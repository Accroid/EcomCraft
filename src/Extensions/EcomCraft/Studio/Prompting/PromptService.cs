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

/// <summary>Prompt service (F37): a style plus an optional one-line description (Chinese or English) becomes an English prompt through MagicPrompt.
/// The model's answer only ever becomes prompt text: it cannot choose a workflow, parameter or execution server. When the model is unavailable or slow,
/// the template and style defaults are used with a plain notice. Describing reference images is on by default; sending product photos to a vision model is off by default and only administrators can turn it on (A5).</summary>
[API.APIClass("EcomCraft prompt service: styles and one-line description to English prompt.")]
public static partial class PromptService
{
    /// <summary>Longest description accepted.</summary>
    public const int MaxDescriptionLength = 500;

    /// <summary>One visual style users can pick: names, description and the English prompt fragment it adds.</summary>
    public sealed record class Style(string Id, Dictionary<string, string> Name, Dictionary<string, string> Description, string Prompt);

    /// <summary>One product subtype users can pick (F53): names and the English prompt fragment it adds, which describes a fitting scene and never the product itself
    /// (the product is pasted in after generation, so a described one would be drawn as a second one).</summary>
    public sealed record class ProductType(string Id, Dictionary<string, string> Name, string Prompt);

    /// <summary>One product-type category (F53) holding its subtypes.</summary>
    public sealed record class ProductCategory(string Id, Dictionary<string, string> Name, List<ProductType> Subtypes);

    /// <summary>The fixed prompt fragment added when "needs a model" is checked (F53); gender or hand-model choices may extend it later.
    /// It does not mention the product: preserved products are pasted in after generation, and a product in the prompt would be drawn as a second one.</summary>
    public const string ModelFragment = "an elegant model in the scene, natural pose, shallow depth of field";

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

    [API.APIDescription("Read or change whether reference images may be described by the configured vision model (on by default) and whether product photos may be sent to it "
        + "to describe the product (off by default). Returns the provider images would be sent to, the local vision device choice, whether it was moved to the CPU after the GPU hung and the self-test result (state none / running / done / failed, device, seconds). Requires the server-settings permission.",
        "vision_enabled: true, product_vision_enabled: false, vision_provider: \"ollama\", vision_device: \"auto\", vision_moved_to_cpu: false, vision_check: { state: \"done\", device: \"cpu\", seconds: 15.2 }")]
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
                    if (!JsonFile.TryWriteAtomic(SettingsFile, saved.ToString(Formatting.Indented), out string error))
                    {
                        Logs.Error($"EcomCraft could not save the prompt settings: {error}");
                        return StudioApi.Error("提示词设置无法保存到服务器数据目录。", "prompt_settings_failed");
                    }
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
        bool visionOn = VisionEnabled();
        if (visionOn)
        {
            // Opening the settings tests a new model, Ollama version or graphics card right away (once the model is found installed), so the result shows while they are open.
            await CheckVisionAsync(session);
        }
        return new JObject()
        {
            ["vision_enabled"] = VisionEnabled(),
            ["product_vision_enabled"] = ProductVisionEnabled(),
            ["vision_provider"] = settings?["visionbackend"]?.ToString() ?? settings?["backend"]?.ToString() ?? "",
            ["vision_model"] = settings?["visionmodel"]?.ToString() ?? "",
            // The device choice for a local Ollama vision model (F71), and whether "auto" moved it to the CPU because the GPU hung; never the address.
            ["vision_device"] = VisionDeviceRules.Normalize(settings?["visiondevice"]?.ToString()),
            ["vision_moved_to_cpu"] = LLMAPICalls.VisionMovedToCpu(settings),
            // The self-test result (F71 P2): state, device and seconds only.
            ["vision_check"] = visionOn ? await VisionDeviceCheck.StatusAsync(settings) : new JObject() { ["state"] = "none" }
        };
    }
}
