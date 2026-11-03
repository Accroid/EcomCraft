using EcomCraftStudio.Studio.Setup;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Builds one standard workflow's graph from its blueprint, pure of SwarmUI so the published result can be unit tested (T118): converts the
/// blueprint, fills the model loaders with the effective file names (the current recipe's variant, or the administrator's legacy override) and wires
/// the product canvas or the direct-fusion trial's inputs. Which blueprint a kind uses, its inpaint access points, the second pass and the edit
/// encoder all come from the recipe — nothing here is written for one model. Returns a problem code ("missing_blueprint" for an unusable blueprint
/// file, "unsupported" when the recipe or the graph does not fit) plus plain-language notes for the administrator's status list.</summary>
internal static class StandardWorkflowGraph
{
    /// <summary>Loader inputs that take a model file name from the recipe's variant (the diffusion model also serves checkpoint loaders).</summary>
    private static string ModelInput(string input, ModelRecipeRules.ModelFiles files)
    {
        return input switch
        {
            "unet_name" or "ckpt_name" => files?.Unet,
            "clip_name" => files?.TextEncoder,
            "vae_name" => files?.Vae,
            _ => null
        };
    }

    /// <summary>Builds one kind's graph: <paramref name="blueprintJson"/> is the blueprint file's text, <paramref name="addProductInpaint"/> the scene
    /// repainting around the product canvas, <paramref name="editAngles"/> (above zero) the direct-fusion trial's product angle count, and
    /// <paramref name="recipe"/> with <paramref name="files"/> and <paramref name="blueprint"/> the resolved recipe the kind builds from.
    /// <paramref name="recipeBook"/> overrides the converter's startup book (the unit tests pass theirs instead of touching shared state).</summary>
    public static (JObject Prompt, JObject Metadata, JObject Values, string Problem, List<string> Notes) Build(string blueprintJson,
        bool addProductInpaint, int editAngles, ModelRecipeRules.Recipe recipe, ModelRecipeRules.ModelFiles files,
        ModelRecipeRules.BlueprintEntry blueprint, string baseDirectory, string[] modelRoots, List<ModelRecipeRules.Recipe> recipeBook = null)
    {
        List<string> notes = [];
        JObject canvas;
        try
        {
            canvas = string.IsNullOrWhiteSpace(blueprintJson) ? null : JToken.Parse(blueprintJson) as JObject;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            canvas = null;
        }
        if (canvas is null)
        {
            return (null, null, null, "missing_blueprint", notes);
        }
        JObject prompt = NativeWorkflowConverter.Convert(canvas, out JObject metadata, out JObject values, out Dictionary<string, string> canvasIds, recipeBook);
        if (prompt is null)
        {
            return (null, null, null, "unsupported", notes);
        }
        foreach (JProperty property in metadata.Properties())
        {
            string input = property.Value["native_input"]?.ToString() ?? "";
            if (property.Value.Value<bool?>("native_model") == true && ModelInput(input, files) is string chosen && !string.IsNullOrWhiteSpace(chosen))
            {
                values[property.Name] = chosen;
                property.Value["default"] = chosen;
                // The diffusion-model loader class must match the file actually in effect (T118b): a GGUF quantization loads through
                // UnetLoaderGGUF, a safetensors through UNETLoader — one recipe may declare both kinds of file (the official int8 file and
                // the community GGUF quant), so the blueprint's own node follows the variant or override instead of the other way around.
                if (input is "unet_name" or "ckpt_name" && property.Value["native_node"]?.ToString() is string loaderId
                    && prompt[loaderId] is JObject loader)
                {
                    string loaderClass = ModelRecipeRules.UnetLoaderClass(chosen);
                    if (loader["class_type"]?.ToString() != loaderClass)
                    {
                        loader["class_type"] = loaderClass;
                        JObject loaderInputs = (JObject)loader["inputs"];
                        if (loaderClass == "UNETLoader")
                        {
                            loaderInputs["weight_dtype"] ??= "default";
                        }
                        else
                        {
                            loaderInputs.Remove("weight_dtype");
                        }
                        property.Value["native_class"] = loaderClass;
                    }
                }
            }
            // The dropdown offers every model the server actually has (A19), keeping the recipe's pick or the blueprint's saved model as the default.
            JArray available = ModelFileScan.Choices(input, values[property.Name], baseDirectory, modelRoots);
            if (available.Count > 0)
            {
                property.Value["values"] = available;
            }
            // The blueprint's saved seed would make every generation with the same prompt the same image; -1 picks a new one each time, and a redo still sets its own.
            if (property.Value.Value<bool?>("native_seed") == true)
            {
                values[property.Name] = -1;
                property.Value["default"] = -1;
            }
        }
        if (editAngles > 0)
        {
            // The edit trial (F84) wires its own hidden SwarmLoadImageB64 inputs (the reference and up to three product angles) and the pixel-budget
            // parameter instead; its product inputs are white-flattened photos, so the transparent product canvas does not apply.
            if (recipe?.ReferenceEdit is null)
            {
                notes.Add("当前配方没有声明直接融合（reference_edit）的编码器和像素预算。");
                return (null, null, null, "unsupported", notes);
            }
            if (ModelRecipeRules.ImageTag(recipe.PromptSyntax, 1) is null)
            {
                notes.Add("当前配方的 prompt_syntax 没有图片序号，无法生成直接融合的提示词。");
                return (null, null, null, "unsupported", notes);
            }
            if (!ReferenceEditRules.Apply(prompt, metadata, values, editAngles, recipe.ReferenceEdit))
            {
                notes.Add("蓝图与配方的直接融合声明不匹配（缺少唯一的编辑编码器或像素预算输入）。");
                return (null, null, null, "unsupported", notes);
            }
            return (prompt, metadata, values, null, notes);
        }
        metadata[ProductInpaintRules.ProductParam] = ProductInpaintRules.ProductParamMetadata();
        if (addProductInpaint)
        {
            // The recipe declares the blueprint nodes to rewire (T118); without a declaration (the T117 string form) the wiring looks for the
            // graph's single KSampler and VAEDecode itself, as before recipes existed.
            string declaredSampler = (blueprint?.InpaintSampler ?? "").Trim();
            string declaredDecoder = (blueprint?.InpaintDecoder ?? "").Trim();
            string samplerId = AccessNode(canvasIds, declaredSampler, "采样器", notes);
            string decoderId = AccessNode(canvasIds, declaredDecoder, "解码器", notes);
            if ((declaredSampler.Length > 0 && samplerId is null) || (declaredDecoder.Length > 0 && decoderId is null))
            {
                return (null, null, null, "unsupported", notes);
            }
            if (!ProductInpaintRules.Apply(prompt, samplerId, decoderId, recipe?.SecondPass))
            {
                notes.Add("蓝图与配方声明的接入点不匹配（找不到可接线的采样器或解码器）。");
                return (null, null, null, "unsupported", notes);
            }
        }
        return (prompt, metadata, values, null, notes);
    }

    /// <summary>Resolves one recipe-declared access point (a blueprint canvas node ID) to its converted node ID; an empty declaration returns null
    /// without a note (the wiring then searches the graph itself), a declaration the blueprint cannot resolve also returns null but with a note.</summary>
    private static string AccessNode(Dictionary<string, string> canvasIds, string declared, string what, List<string> notes)
    {
        if (declared.Length == 0)
        {
            return null;
        }
        if (canvasIds.TryGetValue(declared, out string apiId))
        {
            return apiId;
        }
        notes.Add($"配方声明的{what}节点 {declared} 不在蓝图中。");
        return null;
    }
}
