using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>The product-repainting wiring of the standard workflows (N10, R13), extracted from <see cref="StandardWorkflows"/> so the generated graph
/// can be unit tested without SwarmUI: the product canvas is read by a SwarmLoadImageB64 node straight from the request (A34 — the image never goes
/// through the execution server's input folder), encoded with the graph's own VAE, and its alpha (the node's mask output: 1 where transparent), grown
/// a little into the product, limits sampling to the scene; the sampler starts from it instead of the empty latent. Then the product is put on the
/// decoded scene and the whole picture is sampled again at low strength, so product and scene share their light; the restored details come from the
/// local paste afterwards.</summary>
public static class ProductInpaintRules
{
    /// <summary>The ComfyUI node that reads the product canvas from the request instead of the server's input folder (A34); execution servers without it cannot run these workflows.</summary>
    public const string ImageNode = "SwarmLoadImageB64";

    /// <summary>Node IDs of the added nodes, above the converter's numbering (at most 2000).</summary>
    private const string LoadNode = "9001", EncodeNode = "9002", MaskNode = "9003", GrowNode = "9004", InvertNode = "9005", CompositeNode = "9006",
        HarmonizeEncodeNode = "9007", HarmonizeNode = "9008", HarmonizeDecodeNode = "9009";

    /// <summary>How far the repainted area reaches into the product's edge, in pixels, so the canvas fill around the product cannot leave a halo.</summary>
    private const int MaskGrowPx = 12;

    /// <summary>Strength of the second pass over the whole picture: enough to unify light and edges, little enough to keep the composition.</summary>
    private const double HarmonizeDenoise = 0.35;

    /// <summary>Steps the second pass really runs. KSampler runs its full step count whatever the denoise, so reusing the first pass's steps would double the time.</summary>
    private const int HarmonizeSteps = 10;

    /// <summary>The image parameter the wiring reads; the same ID every standard workflow uses for its product image
    /// (<see cref="StandardWorkflows.ProductImageParam"/>).</summary>
    public const string ProductParam = "ecproductimage";

    /// <summary>The product canvas's workflow parameter (N10): visible to reviewers but never bound to a page field, and marked
    /// <c>image_always_b64</c> so the canvas rides inside the request instead of being uploaded to the execution server's input folder (A34).</summary>
    public static JObject ProductParamMetadata() => new()
    {
        ["id"] = ProductParam, ["name"] = "商品图", ["type"] = "image", ["default"] = "", ["visible"] = true, ["form"] = null,
        ["image_always_b64"] = true,
        ["description"] = "放好商品的透明画布：只重绘商品以外的场景，出图后由平台把原商品贴回画面。"
    };

    /// <summary>Turns the converted text-to-image graph into generation around the product (N10), as described on the class. False when the graph has no
    /// single sampler and decoder to rewire.</summary>
    public static bool Apply(JObject prompt)
    {
        List<JObject> samplers = [.. prompt.Properties().Select(property => property.Value).OfType<JObject>().Where(node => node["class_type"]?.ToString() == "KSampler")];
        List<JProperty> decoders = [.. prompt.Properties().Where(property => property.Value["class_type"]?.ToString() == "VAEDecode")];
        if (samplers.Count != 1 || decoders.Count != 1 || decoders[0].Value["inputs"]?["vae"] is not JArray vae || samplers[0]["inputs"] is not JObject samplerInputs)
        {
            return false;
        }
        string decoder = decoders[0].Name;
        // What saved the first pass's picture now saves the second pass's.
        foreach (JProperty input in prompt.Properties().Select(property => property.Value["inputs"]).OfType<JObject>().SelectMany(inputs => inputs.Properties()))
        {
            if (input.Value is JArray link && link.Count == 2 && link[0].ToString() == decoder)
            {
                input.Value = new JArray(HarmonizeDecodeNode, link[1].DeepClone());
            }
        }
        prompt[LoadNode] = new JObject() { ["class_type"] = ImageNode, ["inputs"] = new JObject() { ["image_base64"] = "${" + ProductParam + "}" } };
        prompt[EncodeNode] = new JObject() { ["class_type"] = "VAEEncode", ["inputs"] = new JObject() { ["pixels"] = new JArray(LoadNode, 0), ["vae"] = vae.DeepClone() } };
        prompt[GrowNode] = new JObject() { ["class_type"] = "GrowMask", ["inputs"] = new JObject() { ["mask"] = new JArray(LoadNode, 1), ["expand"] = MaskGrowPx, ["tapered_corners"] = true } };
        prompt[MaskNode] = new JObject() { ["class_type"] = "SetLatentNoiseMask", ["inputs"] = new JObject() { ["samples"] = new JArray(EncodeNode, 0), ["mask"] = new JArray(GrowNode, 0) } };
        // The empty latent stays in the graph, unconnected, so the width and height parameters keep setting the canvas size.
        JObject harmonizeInputs = (JObject)samplerInputs.DeepClone();
        samplerInputs["latent_image"] = new JArray(MaskNode, 0);
        prompt[InvertNode] = new JObject() { ["class_type"] = "InvertMask", ["inputs"] = new JObject() { ["mask"] = new JArray(LoadNode, 1) } };
        prompt[CompositeNode] = new JObject()
        {
            ["class_type"] = "ImageCompositeMasked",
            ["inputs"] = new JObject()
            {
                ["destination"] = new JArray(decoder, 0), ["source"] = new JArray(LoadNode, 0), ["x"] = 0, ["y"] = 0, ["resize_source"] = false, ["mask"] = new JArray(InvertNode, 0)
            }
        };
        prompt[HarmonizeEncodeNode] = new JObject() { ["class_type"] = "VAEEncode", ["inputs"] = new JObject() { ["pixels"] = new JArray(CompositeNode, 0), ["vae"] = vae.DeepClone() } };
        harmonizeInputs["latent_image"] = new JArray(HarmonizeEncodeNode, 0);
        harmonizeInputs["denoise"] = HarmonizeDenoise;
        harmonizeInputs["steps"] = HarmonizeSteps;
        prompt[HarmonizeNode] = new JObject() { ["class_type"] = "KSampler", ["inputs"] = harmonizeInputs };
        prompt[HarmonizeDecodeNode] = new JObject() { ["class_type"] = "VAEDecode", ["inputs"] = new JObject() { ["samples"] = new JArray(HarmonizeNode, 0), ["vae"] = vae.DeepClone() } };
        return true;
    }
}
