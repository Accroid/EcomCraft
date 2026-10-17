using EcomCraftStudio.Diagnostics;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.WebAPI;

namespace EcomCraftStudio;

/// <summary>Read-only EcomCraft M0 diagnostics, isolated from SwarmUI's generation and scheduling.</summary>
public class EcomCraft : Extension
{
    /// <summary>Version of this diagnostic slice, not a claim of MVP completion.</summary>
    public const string ExtensionVersion = "0.1.0";

    /// <summary>Must match the permission ID derived by SwarmUI for Tabs/Text2Image/EcomCraft.html.</summary>
    public const string DiagnosticsPermissionId = "view_extension_tab_ecomcraft";

    /// <inheritdoc/>
    public override void PopulateMetadata()
    {
        Version = ExtensionVersion;
        ExtensionAuthor = "EcomCraft Studio";
        Description = "EcomCraft M0: read-only administrator node diagnostics; no generation or routing.";
        License = "MIT";
        Tags = ["tabs", "ui"];
    }

    /// <inheritdoc/>
    public override void OnInit()
    {
        // Pre-register before GatherExtensionPageAdditions can assign USER access to this tab.
        PermInfo permission = Permissions.Register(new(DiagnosticsPermissionId, "EcomCraft M0 Diagnostics",
            "Read EcomCraft host configuration and backend observations. Administrators only; does not grant generation, node editing or update access.",
            PermissionDefault.ADMINS, Permissions.GroupExtensionTabs));
        DiagnosticsApi api = new(permission);
        API.RegisterAPICall(api.EcomCraftGetDiagnostics, false, permission);
        ScriptFiles.Add("Assets/ecomcraft.js");
        StyleSheetFiles.Add("Assets/ecomcraft.css");
    }
}
