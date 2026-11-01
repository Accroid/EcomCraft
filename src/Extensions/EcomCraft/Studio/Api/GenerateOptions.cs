namespace EcomCraftStudio.Studio;

/// <summary>Server-side options for one studio generation (R08): internal flags travel in this object, never through client-visible request fields.</summary>
internal sealed class GenerateOptions
{
    /// <summary>Whether the request's prompt is final and must reach the engine as is, without the template's own prefix and suffix (F59 reference mode).</summary>
    public bool RawPrompt;
}
