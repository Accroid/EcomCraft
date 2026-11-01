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


/// <summary>Vision-model status, model choice, device self-test and reference-image description. Part of the PromptService partial class (R11).</summary>
public static partial class PromptService
{
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
            if (state == "ready")
            {
                await StartVisionCheckAsync(settings);
            }
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

    /// <summary>Starts the vision device self-test (F71 P2), which then runs in the background, when the vision model is ready and the current Ollama version,
    /// model and graphics have no result yet. Only called once the status check found the model installed, so a missing model is not tested.</summary>
    private static async Task StartVisionCheckAsync(JObject settings)
    {
        try
        {
            await VisionDeviceCheck.StartAsync(settings, false);
        }
        catch (Exception ex)
        {
            Logs.Debug($"EcomCraft could not start the vision device self-test: {ex.GetType().Name}");
        }
    }

    [API.APIDescription("Test again whether the local Ollama vision model runs faster on the GPU or the CPU (administrators, F71 P2). The test runs in the background with a built-in image; "
        + "read the result from vision_check of EcomCraftPromptSettings. Only for an Ollama vision service with the device set to automatic.",
        "vision_check: { state: \"running\", device: null, seconds: null }")]
    public static async Task<JObject> EcomCraftStudioVisionDeviceCheck(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return StudioApi.Error("只有管理员可以检测看图运行设备。", "bad_permissions");
        }
        if (!VisionEnabled())
        {
            return StudioApi.Error("看图描述已被管理员关闭，无法检测。", "vision_disabled");
        }
        JObject settings = (await SessionSettings.GetMagicPromptSettings())["settings"] as JObject;
        if (!await VisionDeviceCheck.StartAsync(settings, true))
        {
            return StudioApi.Error("只有看图服务是 Ollama、运行设备为“自动”且 Ollama 正在运行时才能检测。", "vision_check_unavailable");
        }
        return new JObject() { ["vision_check"] = await VisionDeviceCheck.StatusAsync(settings) };
    }

    /// <summary>Forgets the cached vision status, after the administrator changed the switch or an install finished.</summary>
    public static void ForgetVisionStatus()
    {
        lock (VisionStatusLock)
        {
            LastVisionStatus = default;
        }
        // A changed model or a finished install may make a test that failed before work now.
        VisionDeviceCheck.ClearFailure();
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
            if (!ImageDataUrl.IsValid(data, ImageDataUrl.ImageDataFormats.AnyImage, MaxReferenceImageLength))
            {
                return StudioApi.Error("参考图须为不超过 5 MB 的图片。", "invalid_reference");
            }
        }
        // One description per image, in parallel; a failed one comes back empty so the page can retry it alone.
        (string english, string chinese)[] answers = await Task.WhenAll(images.Select(async (image, index) =>
        {
            long started = Environment.TickCount64;
            // A short user text keeps the request valid for every backend (some reject image-only or empty-text messages).
            // Small vision models often still name the product; what they wrote about it is removed, or the image model draws a second one.
            string described = PromptRules.WithoutProduct(await AskModelAsync(session, ReferenceInstruction, "Describe the setup of this product photo.", image.ToString()));
            if (described.Length == 0)
            {
                return ("", "");
            }
            string translated = await TranslateAsync(session, "Simplified Chinese", described, DescriptionTranslateTimeout);
            // A translation can fail once on a passing connection error (seen as a TLS handshake failure with a remote text model); one retry fixes it.
            if (translated.Length == 0)
            {
                translated = await TranslateAsync(session, "Simplified Chinese", described, DescriptionTranslateTimeout);
            }
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
}
