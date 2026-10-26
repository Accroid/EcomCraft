using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio;
using SwarmUI.Text2Image;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.WebAPI;

namespace EcomCraftStudio;

/// <summary>EcomCraft creative workspace and local workflow integration.</summary>
public class EcomCraft : Extension
{
    /// <summary>Extension version, not a claim of MVP completion or production readiness.</summary>
    public const string ExtensionVersion = "0.3.0";

    /// <summary>Allows employees to use only Comfy workflows explicitly published to the simple workspace.</summary>
    public static readonly PermInfo PermUsePublishedWorkflows = Permissions.Register(new(
        "ecomcraft_use_published_workflows", "EcomCraft Published Workflow Use",
        "Allows use of administrator-published EcomCraft workflows; does not grant workflow editing or arbitrary workflow submission.",
        PermissionDefault.USER, Permissions.GroupExtensionTabs));

    /// <inheritdoc/>
    public override void PopulateMetadata()
    {
        Version = ExtensionVersion;
        ExtensionAuthor = "EcomCraft Studio";
        Description = "EcomCraft image/video workspace with generation on registered ComfyUI execution servers and published workflows.";
        License = "MIT";
        Tags = ["ui", "workflows"];
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        // Once enabled and installed, the root URL lands on this Studio (see WebServer root route).
        Program.EcomCraftWantsHome = true;
        // Narrowly expose only workflows published for the simple workspace; do not grant SwarmUI's broad Comfy permissions.
        T2IParamTypes.ParameterPermissionOverrideValidators.Add(StudioApi.PublishedWorkflowPermissionOverride);
        T2IParamTypes.FakeTypeProviders.Add(StudioApi.PublishedWorkflowDynamicParamProvider);
        // Built-in MagicPrompt: registers multi-backend LLM prompt-enhancement APIs and API-key slots.
        MagicPromptAPI.Register();
        ScriptFiles.Add("Assets/studio-entry.js");
        OtherAssets.Add("Studio/studio.css");
        OtherAssets.Add("Studio/studio.js");
        API.RegisterAPICall(StudioApi.EcomCraftStudioCatalog, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioBackendStatus, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioQueue, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioCancelTask, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(StudioApi.EcomCraftStudioGenerate, true, Permissions.BasicImageGeneration);
        T2IEngine.AltBackendValidators.Add(StudioApi.ValidateExecutionBackend);
        T2IEngine.PreGenerateEvent += StudioApi.PrepareNativeWorkflowSeeds;
        ComfyUIAPIAbstractBackend.QueueProgressObserved += StudioApi.ObserveComfyProgress;
    }

    /// <inheritdoc/>
    public override void OnPreLaunch()
    {
        StudioRoutes.Map(this);
    }

    /// <inheritdoc/>
    public override void OnShutdown()
    {
        T2IParamTypes.ParameterPermissionOverrideValidators.Remove(StudioApi.PublishedWorkflowPermissionOverride);
        T2IParamTypes.FakeTypeProviders.Remove(StudioApi.PublishedWorkflowDynamicParamProvider);
        T2IEngine.AltBackendValidators.Remove(StudioApi.ValidateExecutionBackend);
        T2IEngine.PreGenerateEvent -= StudioApi.PrepareNativeWorkflowSeeds;
        ComfyUIAPIAbstractBackend.QueueProgressObserved -= StudioApi.ObserveComfyProgress;
        WorkflowDiscovery.Shutdown();
    }
}
