using EcomCraftStudio.Studio.Setup;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>The product-repainting wiring of the standard workflows (N10, R13), extracted from <see cref="StandardWorkflows"/> so the generated graph
/// can be unit tested without SwarmUI: the product canvas is read by a SwarmLoadImageB64 node straight from the request (A34 — the image never goes
/// through the execution server's input folder), encoded with the graph's own VAE, and its alpha (the node's mask output: 1 where transparent), grown
/// a little into the product, limits sampling to the scene; the sampler starts from it instead of the empty latent. Then the product is put on the
/// decoded scene and the whole picture is sampled again at low strength, so product and scene share their light; the restored details come from the
/// local paste afterwards. Which sampler and decoder to rewire comes from the recipe's blueprint access points (T118) — falling back to the graph's
/// single KSampler and VAEDecode when the recipe declares none — and both sampler kinds the blueprints use are supported: KSampler, and
/// SamplerCustomAdvanced, whose second pass copies its sigmas chain with new values — steps and denoise on a BasicScheduler-style node, or the
/// recipe's tail table on the official Turbo path's ManualSigmas (T118b).</summary>
public static class ProductInpaintRules
{
    /// <summary>The ComfyUI node that reads the product canvas from the request instead of the server's input folder (A34); execution servers without it cannot run these workflows.</summary>
    public const string ImageNode = "SwarmLoadImageB64";

    /// <summary>Node IDs of the added nodes, above the converter's numbering (at most 2000).</summary>
    private const string LoadNode = "9001", EncodeNode = "9002", MaskNode = "9003", GrowNode = "9004", InvertNode = "9005", CompositeNode = "9006",
        HarmonizeEncodeNode = "9007", HarmonizeNode = "9008", HarmonizeDecodeNode = "9009", HarmonizeSchedulerNode = "9010";

    /// <summary>How far the repainted area reaches into the product's edge, in pixels, so the canvas fill around the product cannot leave a halo.</summary>
    private const int MaskGrowPx = 12;

    /// <summary>Strength of the second pass over the whole picture when no recipe declares one: enough to unify light and edges, little enough to keep the composition.</summary>
    private const double DefaultHarmonizeDenoise = 0.35;

    /// <summary>Steps the second pass really runs when no recipe declares them. KSampler runs its full step count whatever the denoise, so reusing the first pass's steps would double the time.</summary>
    private const int DefaultHarmonizeSteps = 10;

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

    /// <summary>Turns the converted text-to-image graph into generation around the product (N10), as described on the class. The sampler and decoder
    /// to rewire are the recipe-declared access points (blueprint canvas node IDs resolved to converted IDs, T118), or — when the recipe declares
    /// none — the graph's single KSampler and VAEDecode, as before recipes existed. The second pass follows the recipe's steps and denoise
    /// (<paramref name="secondPass"/>), defaulting to the pre-recipe values. False when the declared or found nodes do not fit the wiring.</summary>
    /// <param name="prompt">The converted graph, modified in place.</param>
    /// <param name="samplerId">The converted node ID of the sampler to rewire; null or empty to look for the single KSampler.</param>
    /// <param name="decoderId">The converted node ID of the decoder whose picture is repainted; null or empty to look for the single VAEDecode.</param>
    /// <param name="secondPass">The recipe's second-pass steps and denoise; null keeps the defaults.</param>
    public static bool Apply(JObject prompt, string samplerId = null, string decoderId = null, ModelRecipeRules.SecondPassRule secondPass = null)
    {
        double denoise = secondPass?.Denoise > 0 ? secondPass.Denoise : DefaultHarmonizeDenoise;
        int steps = secondPass?.Steps > 0 ? secondPass.Steps.Value : DefaultHarmonizeSteps;
        string sigmas = secondPass?.Sigmas ?? "";
        JProperty sampler = Node(prompt, samplerId);
        if (sampler is null && !string.IsNullOrWhiteSpace(samplerId))
        {
            // A declared access point that the graph does not have is a recipe error, not a reason to guess another node.
            return false;
        }
        if (sampler is null)
        {
            // The pre-recipe fallback (T117 string form): rewire the graph's single KSampler, exactly as before access points existed.
            List<JProperty> samplers = [.. prompt.Properties().Where(property => (property.Value as JObject)?["class_type"]?.ToString() == "KSampler")];
            sampler = samplers.Count == 1 ? samplers[0] : null;
        }
        else if (sampler.Value["class_type"]?.ToString() is not ("KSampler" or "SamplerCustomAdvanced"))
        {
            return false;
        }
        JProperty decoder = Node(prompt, decoderId);
        if (decoder is null && !string.IsNullOrWhiteSpace(decoderId))
        {
            return false;
        }
        if (decoder is null)
        {
            List<JProperty> decoders = [.. prompt.Properties().Where(property => (property.Value as JObject)?["class_type"]?.ToString() == "VAEDecode")];
            decoder = decoders.Count == 1 ? decoders[0] : null;
        }
        else if (decoder.Value["class_type"]?.ToString() != "VAEDecode")
        {
            return false;
        }
        if (sampler is null || decoder is null || decoder.Value["inputs"]?["vae"] is not JArray vae || sampler.Value["inputs"] is not JObject samplerInputs)
        {
            return false;
        }
        string decoderName = decoder.Name;
        // What saved the first pass's picture now saves the second pass's.
        foreach (JProperty input in prompt.Properties().Select(property => property.Value["inputs"]).OfType<JObject>().SelectMany(inputs => inputs.Properties()))
        {
            if (input.Value is JArray link && link.Count == 2 && link[0].ToString() == decoderName)
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
                ["destination"] = new JArray(decoderName, 0), ["source"] = new JArray(LoadNode, 0), ["x"] = 0, ["y"] = 0, ["resize_source"] = false, ["mask"] = new JArray(InvertNode, 0)
            }
        };
        prompt[HarmonizeEncodeNode] = new JObject() { ["class_type"] = "VAEEncode", ["inputs"] = new JObject() { ["pixels"] = new JArray(CompositeNode, 0), ["vae"] = vae.DeepClone() } };
        harmonizeInputs["latent_image"] = new JArray(HarmonizeEncodeNode, 0);
        if (!WireSecondPassSampler(prompt, harmonizeInputs, steps, denoise, sigmas))
        {
            return false;
        }
        prompt[HarmonizeNode] = new JObject() { ["class_type"] = sampler.Value["class_type"]?.ToString(), ["inputs"] = harmonizeInputs };
        prompt[HarmonizeDecodeNode] = new JObject() { ["class_type"] = "VAEDecode", ["inputs"] = new JObject() { ["samples"] = new JArray(HarmonizeNode, 0), ["vae"] = vae.DeepClone() } };
        return true;
    }

    /// <summary>The prompt node with this ID, when it exists; null otherwise (also for a null or empty ID).</summary>
    private static JProperty Node(JObject prompt, string id) =>
        string.IsNullOrWhiteSpace(id) ? null : prompt.Properties().FirstOrDefault(property => property.Name == id);

    /// <summary>Prepares the second pass's sampler inputs (<paramref name="harmonizeInputs"/>, the cloned first-pass inputs): a KSampler runs its own
    /// steps and denoise, while an advanced sampler keeps its noise, guider and sampler chains and gets a copy of its sigmas chain with the second
    /// pass's values — a scheduler node with steps and denoise (such as BasicScheduler) takes those, and a fixed sigma table (ManualSigmas, the
    /// official Turbo path) takes the recipe's tail table (<paramref name="sigmas"/>). False when neither can be set.</summary>
    private static bool WireSecondPassSampler(JObject prompt, JObject harmonizeInputs, int steps, double denoise, string sigmas)
    {
        if (harmonizeInputs["steps"] is not null)
        {
            harmonizeInputs["steps"] = steps;
            harmonizeInputs["denoise"] = denoise;
            return true;
        }
        if (harmonizeInputs["sigmas"] is not JArray sigmasLink || sigmasLink.Count != 2 || sigmasLink[1].Type != JTokenType.Integer
            || prompt[sigmasLink[0].ToString()] is not JObject sigmasNode || sigmasNode["inputs"] is not JObject sigmasInputs)
        {
            return false;
        }
        // A scheduler node with steps and denoise: the copied chain runs the second pass at its own strength.
        if (sigmasInputs["steps"] is JValue && sigmasInputs["denoise"] is JValue)
        {
            JObject copiedScheduler = (JObject)sigmasNode.DeepClone();
            JObject copiedInputs = (JObject)copiedScheduler["inputs"];
            copiedInputs["steps"] = steps;
            copiedInputs["denoise"] = denoise;
            prompt[HarmonizeSchedulerNode] = copiedScheduler;
            harmonizeInputs["sigmas"] = new JArray(HarmonizeSchedulerNode, sigmasLink[1].DeepClone());
            return true;
        }
        // A fixed sigma table (ManualSigmas): the second pass runs over the recipe's tail of the official table, a fixed short schedule
        // that plays the role steps and denoise play elsewhere (T118b).
        if (sigmasInputs["sigmas"] is JValue && sigmas.Length > 0)
        {
            JObject copiedTable = (JObject)sigmasNode.DeepClone();
            ((JObject)copiedTable["inputs"])["sigmas"] = sigmas;
            prompt[HarmonizeSchedulerNode] = copiedTable;
            harmonizeInputs["sigmas"] = new JArray(HarmonizeSchedulerNode, sigmasLink[1].DeepClone());
            return true;
        }
        return false;
    }
}
