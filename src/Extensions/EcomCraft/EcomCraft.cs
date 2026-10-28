using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
using EcomCraftStudio.Studio.PostProcess;
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
    public const string ExtensionVersion = "0.6.0";

    /// <summary>Allows employees to use only Comfy workflows explicitly published to the simple workspace.</summary>
    public static readonly PermInfo PermUsePublishedWorkflows = Permissions.Register(new(
        "ecomcraft_use_published_workflows", "EcomCraft Published Workflow Use",
        "Allows use of administrator-published EcomCraft workflows; does not grant workflow editing or arbitrary workflow submission.",
        PermissionDefault.USER, Permissions.GroupExtensionTabs));

    /// <summary>Allows submitting workflows found on execution servers for review and seeing one's own submission results.</summary>
    public static readonly PermInfo PermSubmitWorkflows = Permissions.Register(new(
        "ecomcraft_submit_workflows", "EcomCraft Submit Workflows",
        "Allows submitting ComfyUI workflows saved on execution servers for review, and viewing the state and rejection reasons of one's own submissions.",
        PermissionDefault.USER, Permissions.GroupExtensionTabs));

    /// <summary>Allows reviewing, publishing, rejecting and unpublishing workflows and configuring their user-visible parameters.</summary>
    public static readonly PermInfo PermReviewWorkflows = Permissions.Register(new(
        "ecomcraft_review_workflows", "EcomCraft Review Workflows",
        "Allows reviewing discovered EcomCraft workflows: configure user-visible parameters, publish frozen snapshots, reject with a reason, or unpublish.",
        PermissionDefault.ADMINS, Permissions.GroupExtensionTabs));

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
        // studio.js / studio.css are served by StudioRoutes from disk, so they always match the page after an update.
        API.RegisterAPICall(StudioApi.EcomCraftStudioCatalog, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioBackendStatus, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioQueue, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioCancelTask, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(StudioApi.EcomCraftStudioGenerate, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(WorkflowReviewApi.EcomCraftStudioReviewList, false, PermReviewWorkflows);
        API.RegisterAPICall(WorkflowReviewApi.EcomCraftStudioReviewDetail, false, PermReviewWorkflows);
        API.RegisterAPICall(WorkflowReviewApi.EcomCraftStudioReviewDecide, true, PermReviewWorkflows);
        API.RegisterAPICall(WorkflowReviewApi.EcomCraftStudioWorkflowSubmissions, false, PermSubmitWorkflows);
        API.RegisterAPICall(WorkflowReviewApi.EcomCraftStudioSubmitWorkflow, true, PermSubmitWorkflows);
        API.RegisterAPICall(StorageApi.EcomCraftStorageSettings, false, Permissions.EditServerSettings);
        API.RegisterAPICall(StorageApi.EcomCraftStorageSave, true, Permissions.EditServerSettings);
        API.RegisterAPICall(StorageApi.EcomCraftStorageMigrate, true, Permissions.EditServerSettings);
        API.RegisterAPICall(StorageApi.EcomCraftStoragePending, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(TemplateApi.EcomCraftStudioTemplates, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(TemplateApi.EcomCraftStudioPlatforms, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(PromptService.EcomCraftStudioStyles, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(PromptService.EcomCraftStudioComposePrompt, false, Permissions.BasicImageGeneration);
        API.RegisterAPICall(PromptService.EcomCraftPromptSettings, true, Permissions.EditServerSettings);
        API.RegisterAPICall(CutoutService.EcomCraftStudioCutout, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(EcomCraftStudio.Studio.Products.ProductLibrary.EcomCraftStudioProducts, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(EcomCraftStudio.Studio.Products.ProductLibrary.EcomCraftStudioProductSave, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(EcomCraftStudio.Studio.Products.ProductLibrary.EcomCraftStudioProductUpdate, true, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(EcomCraftStudio.Studio.Products.ProductLibrary.EcomCraftStudioProductDelete, true, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(EcomCraftStudio.Studio.Generation.BatchService.EcomCraftStudioGenerateSet, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(EcomCraftStudio.Studio.Generation.BatchService.EcomCraftStudioBatchStatus, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(EcomCraftStudio.Studio.Generation.BatchService.EcomCraftStudioBatchRetry, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(EcomCraftStudio.Studio.Generation.BatchService.EcomCraftStudioBatchCancel, true, Permissions.BasicImageGeneration);
        API.RegisterAPICall(TemplateAdminApi.EcomCraftTemplateAdminList, false, PermReviewWorkflows);
        API.RegisterAPICall(TemplateAdminApi.EcomCraftTemplateSave, true, PermReviewWorkflows);
        API.RegisterAPICall(TemplateAdminApi.EcomCraftTemplateDelete, true, PermReviewWorkflows);
        T2IEngine.AltBackendValidators.Add(StudioApi.ValidateExecutionBackend);
        T2IEngine.PreGenerateEvent += StudioApi.PrepareNativeWorkflowSeeds;
        ComfyUIAPIAbstractBackend.QueueProgressObserved += StudioApi.ObserveComfyProgress;
        WorkflowDiscovery.Start();
        WorkSync.Start();
        TemplateStore.Init(FilePath);
        PromptService.Init(FilePath);
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
        WorkSync.Shutdown();
    }
}
