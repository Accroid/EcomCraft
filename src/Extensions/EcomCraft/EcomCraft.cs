using EcomCraftStudio.Diagnostics;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio;
using SwarmUI.Text2Image;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.WebAPI;

namespace EcomCraftStudio;

/// <summary>EcomCraft creative workspace and separate read-only administrator diagnostics.</summary>
public class EcomCraft : Extension
{
    /// <summary>Extension version, not a claim of MVP completion or production readiness.</summary>
    public const string ExtensionVersion = "0.3.0";

    /// <summary>Must match the permission ID derived by SwarmUI for Tabs/Text2Image/EcomCraft.html.</summary>
    public const string DiagnosticsPermissionId = "view_extension_tab_ecomcraft";

    /// <inheritdoc/>
    public override void PopulateMetadata()
    {
        Version = ExtensionVersion;
        ExtensionAuthor = "EcomCraft Studio";
        Description = "EcomCraft image/video workspace with local-only generation adapter and read-only administrator diagnostics.";
        License = "MIT";
        Tags = ["tabs", "ui"];
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        // Once enabled and installed, the root URL lands on this Studio (see WebServer root route).
        Program.EcomCraftWantsHome = true;
        // Built-in MagicPrompt: registers multi-backend LLM prompt-enhancement APIs and API-key slots.
        MagicPromptAPI.Register();
        // Pre-register before GatherExtensionPageAdditions can assign USER access to this tab.
        PermInfo permission = Permissions.Register(new(DiagnosticsPermissionId, "EcomCraft M0 Diagnostics",
            "Read EcomCraft host configuration and backend observations. Administrators only; does not grant generation, node editing or update access.",
            PermissionDefault.ADMINS, Permissions.GroupExtensionTabs));
        DiagnosticsApi api = new(permission);
        API.RegisterAPICall(api.EcomCraftGetDiagnostics, false, permission);
        ScriptFiles.Add("Assets/ecomcraft.js");
        StyleSheetFiles.Add("Assets/ecomcraft.css");
        ScriptFiles.Add("Assets/studio-entry.js");
        OtherAssets.Add("Studio/studio.css");
        OtherAssets.Add("Studio/studio.js");
        API.RegisterAPICall(StudioApi.EcomCraftStudioCatalog, false, Permissions.FundamentalGenerateTabAccess);
        API.RegisterAPICall(StudioApi.EcomCraftStudioGenerate, true, Permissions.BasicImageGeneration);
        T2IEngine.AltBackendValidators.Add(StudioApi.ValidateLocalBackend);
    }

    /// <inheritdoc/>
    public override void OnPreLaunch()
    {
        StudioRoutes.Map(this);
    }
}
