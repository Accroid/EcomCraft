using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Server-side options for one studio generation (R08): internal flags travel in this object, never through client-visible request fields.</summary>
internal sealed class GenerateOptions
{
    /// <summary>Whether the request's prompt is final and must reach the engine as is, without the template's own prefix and suffix (F59 reference mode).</summary>
    public bool RawPrompt;

    /// <summary>Whether the prompt may carry the current recipe's image tags (<c>prompt_syntax</c>, T118): the reference-edit trial's prompt is
    /// written by the server with them (F84). Only the exact tags are allowed; a client can never set this flag, and any other native syntax is still refused.</summary>
    public bool AllowImageTagPrompt;

    /// <summary>Server-chosen published workflow ID that replaces the template's plan workflow for this request (the reference-edit trial picks one per
    /// product angle count, F84); null keeps the template's own workflow.</summary>
    public string WorkflowOverride;

    /// <summary>Workflow parameters the server itself fills for this request (the reference-edit trial's images and pixel budget, F84). They are validated
    /// against the published definitions like any parameter but bypass the page allowlist, which never lists them; a client cannot set them.</summary>
    public JObject ServerParams;
}
