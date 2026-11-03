using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.Setup;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Small adapter limited to registered ComfyUI execution servers. This is not a durable task service or a platform-wide egress policy.</summary>
[API.APIClass("EcomCraft image and text-to-video workspace with generation on registered ComfyUI execution servers and optional cloud prompt enhancement.")]
public static partial class StudioApi
{
    /// <summary>Extra-metadata key marking a request built by this workspace.</summary>
    public const string ExecutionScopeKey = "ecomcraft_execution_scope";

    /// <summary>Value of <see cref="ExecutionScopeKey"/> that restricts a request to registered execution servers.</summary>
    public const string ExecutionScopeValue = "registered_comfy";

    /// <summary>Upper bound for LoRAs in one standard request.</summary>
    private const int MaxLoras = 5;

    /// <summary>Extra-metadata key carrying the published workflow selected for this request.</summary>
    public const string PublishedWorkflowScopeKey = "ecomcraft_published_workflow";

    /// <summary>Extra-metadata key carrying the execution server ID the scheduler chose; set by the server only, never copied from the client.</summary>
    public const string ExecutionServerKey = "ecomcraft_execution_server";

    internal static bool CanView(Session session) => session?.User is not null
        && session.User.HasPermission(Permissions.FundamentalGenerateTabAccess)
        && session.User.HasPermission(Permissions.FundamentalModelAccess)
        && session.User.HasPermission(Permissions.ModelParams);

    /// <summary>Returns whether the selected MagicPrompt backend has a configured cloud credential.</summary>
    private static bool IsCloudModelConfigured(Session session)
    {
        try
        {
            JObject settingsResponse = SessionSettings.GetMagicPromptSettings().GetAwaiter().GetResult();
            string backend = settingsResponse["settings"]?["backend"]?.ToString()?.ToLowerFast();
            string keyType = backend switch
            {
                "openai" => "openai_api",
                "anthropic" => "anthropic_api",
                "openrouter" => "openrouter_api",
                "grok" => "grok_api",
                _ => null
            };
            if (keyType is null)
            {
                return false;
            }
            string key = session.User.GetGenericData(keyType, "key")
                ?? Program.Sessions.GenericSharedUser.GetGenericData(keyType, "key");
            return !string.IsNullOrWhiteSpace(key);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<T2IModel> LocalModels(Session session) =>
        Program.T2IModelSets.TryGetValue("Stable-Diffusion", out T2IModelHandler handler)
            ? handler.Models.Values.Where(m => m.IsSupportedModelType && session.User.IsAllowedModel(m.Name)) : [];

    private static List<string> LocalLoras(Session session) =>
        Program.T2IModelSets.TryGetValue("LoRA", out T2IModelHandler handler)
            ? handler.ListModelNamesFor(session).Where(n => n != "(None)").ToList() : [];

    private static JArray OptionPairs(IEnumerable<string> values)
    {
        JArray array = new();
        foreach (string value in values)
        {
            string id = value.Before("///");
            string name = value.Contains("///") ? value.After("///") : value;
            array.Add(new JObject() { ["id"] = id, ["name"] = name });
        }
        return array;
    }

    private static string Kind(T2IModel model)
    {
        T2IModelCompatClass compat = model.ModelClass?.CompatClass;
        if (compat is null || compat.IsAudioModel)
        {
            return null;
        }
        if (compat.IsText2Video)
        {
            return "video";
        }
        return compat.IsImage2Video ? null : "image";
    }

    /// <summary>Counts execution servers by state; busy online servers still count, as requests queue for them. Field names are kept from the single-machine version for client compatibility.</summary>
    private static JObject LocalBackendStatus() => new()
    {
        ["local_backends_observed"] = ExecutionServers.All.Count(ExecutionServers.IsReady),
        ["local_backends_starting"] = ExecutionServers.All.Count(data => data.Backend.IsEnabled
            && data.Backend.Status is BackendStatus.WAITING or BackendStatus.LOADING),
        ["local_backends_errored"] = ExecutionServers.All.Count(data => data.Backend.IsEnabled && data.Backend.Status == BackendStatus.ERRORED)
    };

    [API.APIDescription("Read the current availability of registered ComfyUI execution servers.", "local_backends_observed: 0, local_backends_starting: 0, local_backends_errored: 0")]
    public static Task<JObject> EcomCraftStudioBackendStatus(Session session)
    {
        return Task.FromResult(CanView(session) ? LocalBackendStatus() : Error("无权访问创作工作台。", "bad_permissions"));
    }

    [API.APIDescription("Read authorized model metadata, discovered workflows and execution server availability. Workflow discovery runs in the background; no cloud calls.", "models: [], local_backends_observed: 0, can_generate: false, cloud_configured: false, generation_pixels: 1048576")]
    public static async Task<JObject> EcomCraftStudioCatalog(Session session)
    {
        if (!CanView(session))
        {
            return Error("无权访问创作工作台或模型列表。", "bad_permissions");
        }
        WorkflowDiscovery.EnsureFresh();
        JObject result = new()
        {
            ["user_name"] = session.User.UserID,
            ["version"] = EcomCraft.ExtensionVersion,
            ["can_generate"] = session.User.HasPermission(Permissions.BasicImageGeneration),
            ["can_generate_video"] = session.User.HasPermission(Permissions.BasicImageGeneration) && session.User.HasPermission(Permissions.ParamVideo),
            ["can_manage_settings"] = session.User.HasPermission(Permissions.EditServerSettings),
            ["can_delete_images"] = session.User.HasPermission(Permissions.UserDeleteImage),
            ["can_review_workflows"] = session.User.HasPermission(EcomCraft.PermReviewWorkflows),
            ["can_submit_workflows"] = session.User.HasPermission(EcomCraft.PermSubmitWorkflows),
            ["cloud_configured"] = IsCloudModelConfigured(session),
            ["models"] = new JArray(LocalModels(session).Where(m => Kind(m) is not null).OrderBy(m => m.Name).Select(m => new JObject()
            {
                ["id"] = m.Name,
                ["kind"] = Kind(m),
                ["class_name"] = m.ModelClass.Name,
                ["width"] = m.StandardWidth > 0 ? m.StandardWidth : m.ModelClass.StandardWidth,
                ["height"] = m.StandardHeight > 0 ? m.StandardHeight : m.ModelClass.StandardHeight,
                ["resolution_step"] = Math.Max(8, m.ModelClass.CompatClass.ResolutionPrecision),
                ["image_reference"] = Kind(m) == "image"
            })),
            ["loras"] = new JArray(LocalLoras(session).OrderBy(n => n)),
            ["samplers"] = OptionPairs(ComfyUIBackendExtension.Samplers),
            ["schedulers"] = OptionPairs(ComfyUIBackendExtension.Schedulers),
            ["workflows"] = PublishedWorkflowCatalog(session),
            // The professional mode's recommended resolution (T114): the current recipe's pixel count for this machine's tier; only a guide (A19).
            ["generation_pixels"] = ModelRecipes.Current().Recipe?.GenerationPixels.For(await HardwareProbe.CurrentTierAsync()) ?? OutputSizeRules.DefaultGenerationPixels
        };
        foreach (KeyValuePair<string, JToken> entry in LocalBackendStatus())
        {
            result[entry.Key] = entry.Value;
        }
        return result;
    }

    internal static JObject Error(string text, string id) => new() { ["error"] = text, ["error_id"] = id };
}
