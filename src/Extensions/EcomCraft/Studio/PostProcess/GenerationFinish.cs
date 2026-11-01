using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

namespace EcomCraftStudio.Studio.PostProcess;

/// <summary>The local post-processing steps applied when a generation returns (R09): scaling to the chosen size first, then pasting the
/// original product pixels back at that size, so scaling never blurs the product's logo or text (the order the c2fab63c fix relies on).
/// Both steps run before the works are synced or shown; this is the seam where product re-location and fidelity checks will attach (T86).</summary>
internal static class GenerationFinish
{
    /// <summary>Applies every post-processing step to one finished generation result: <see cref="OutputResize"/> first, then
    /// <see cref="ProductPreserve"/>; each step counts its failures into the result instead of failing the whole task.</summary>
    internal static async Task ApplyAsync(User user, string preserveToken, (int Width, int Height)? outputSize, JObject result)
    {
        if (outputSize is (int finalWidth, int finalHeight))
        {
            int failed = await OutputResize.ApplyAsync(user, result, finalWidth, finalHeight);
            if (failed > 0)
            {
                result["resize_failed"] = failed;
            }
        }
        if (preserveToken is not null)
        {
            int failed = await ProductPreserve.ApplyAsync(preserveToken, user, result);
            if (failed > 0)
            {
                result["preserve_failed"] = failed;
            }
        }
    }

    /// <summary>Drops the saved product canvas of a generation that failed, so nothing of it is kept around.</summary>
    internal static void Discard(string preserveToken) => ProductPreserve.Discard(preserveToken);
}
