using Newtonsoft.Json.Linq;
using System.IO;
using EcomCraftStudio.Studio.Setup;

namespace EcomCraftStudio.Studio;

/// <summary>Converts saved canvases and embedded ComfyUI subgraphs into validated server-owned prompts. Model-specific rules (cfg bounds with
/// their hint texts, the negative-prompt note and the latent size step) follow the recipe book whose match conditions the graph satisfies (T118):
/// without a book or a match the generic ranges apply.</summary>
public sealed class NativeWorkflowConverter
{
    /// <summary>Embedded definitions are resolved only from the saved file, never from client input or external files.</summary>
    private readonly Dictionary<string, JObject> Definitions = new();

    /// <summary>The recipe book this conversion reads its model-specific rules from; null falls back to the startup book.</summary>
    private readonly List<ModelRecipeRules.Recipe> Book;

    /// <summary>Converts with the given recipe book (the tests pass theirs instead of touching shared state).</summary>
    private NativeWorkflowConverter(List<ModelRecipeRules.Recipe> book)
    {
        Book = book;
    }

    /// <summary>The flattened executable graph.</summary>
    private readonly JObject Prompt = new();

    /// <summary>Unique node IDs across every instance of an embedded subgraph.</summary>
    private int NextId;

    /// <summary>One instantiated canvas or subgraph, with its own inputs and node identities.</summary>
    private sealed class Scope
    {
        /// <summary>Serialized nodes in this scope.</summary>
        public Dictionary<string, JObject> Nodes = new();
        /// <summary>Normalized links indexed by ID.</summary>
        public Dictionary<string, JArray> Links = new();
        /// <summary>API node IDs allocated in this instance.</summary>
        public Dictionary<string, string> Ids = new();
        /// <summary>Children instantiated from definition IDs.</summary>
        public Dictionary<string, Scope> Children = new();
        /// <summary>Nodes being traversed, for cycle rejection.</summary>
        public HashSet<string> Visiting = new();
        /// <summary>Bindings for the virtual input node; null means use the target widget's saved default.</summary>
        public List<JToken> Bindings = new();
        /// <summary>The graph whose input and output slots define this scope.</summary>
        public JObject Graph;
        /// <summary>Definition ancestry, for recursive definition rejection.</summary>
        public HashSet<string> Ancestors = new();
    }

    /// <summary>Returns null for unsupported or malformed graphs rather than publishing an incomplete workflow.</summary>
    public static JObject Convert(JObject canvas) => Convert(canvas, out _, out _);

    /// <summary>Publishes saved defaults and server-owned bindings alongside the executable template.</summary>
    public static JObject Convert(JObject canvas, out JObject metadata, out JObject values)
    {
        return Convert(canvas, out metadata, out values, out _);
    }

    /// <summary>Also reports the blueprint's canvas node IDs by their converted node IDs (T118): how a recipe's declared access points
    /// (the sampler and decoder to rewire) resolve into the flattened graph. An ID used in two scopes is dropped as ambiguous.
    /// <paramref name="recipeBook"/> overrides the startup book (the unit tests pass theirs instead of touching shared state).</summary>
    public static JObject Convert(JObject canvas, out JObject metadata, out JObject values, out Dictionary<string, string> canvasNodeIds,
        List<ModelRecipeRules.Recipe> recipeBook = null)
    {
        NativeWorkflowConverter converter = new(recipeBook);
        metadata = converter.Parameters;
        values = converter.Values;
        canvasNodeIds = new Dictionary<string, string>();
        try
        {
            JObject prompt = converter.Build(canvas);
            if (prompt is not null)
            {
                canvasNodeIds = new Dictionary<string, string>(converter.ApiByCanvasId);
            }
            return prompt;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Normalizes legacy array links and modern object links, validating IDs and slots.</summary>
    private static Scope CreateScope(JObject graph, HashSet<string> ancestors)
    {
        if (graph?["nodes"] is not JArray nodes || nodes.Count == 0 || nodes.Count > 1000 || graph["links"] is not JArray links)
        {
            throw new InvalidDataException();
        }
        Scope scope = new() { Graph = graph, Ancestors = ancestors };
        foreach (JObject node in nodes.OfType<JObject>())
        {
            string id = node["id"]?.ToString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(node["type"]?.ToString()) || !scope.Nodes.TryAdd(id, node))
            {
                throw new InvalidDataException();
            }
        }
        if (scope.Nodes.Count != nodes.Count)
        {
            throw new InvalidDataException();
        }
        foreach (JToken item in links)
        {
            JArray link = item as JArray;
            if (item is JObject obj)
            {
                link = new JArray(obj["id"], obj["origin_id"], obj["origin_slot"], obj["target_id"], obj["target_slot"]);
            }
            if (link is null || link.Count < 5 || link.Take(5).Any(value => value.Type == JTokenType.Null)
                || link[2].Type != JTokenType.Integer || link[4].Type != JTokenType.Integer
                || link[2].Value<int>() < 0 || link[4].Value<int>() < 0 || !scope.Links.TryAdd(link[0].ToString(), link))
            {
                throw new InvalidDataException();
            }
        }
        return scope;
    }

    /// <summary>Maps widget positions including widgets that are linked and frontend-only seed controls.</summary>
    private static Dictionary<string, JToken> WidgetValues(JObject node)
    {
        Dictionary<string, JToken> result = new();
        JArray values = node["widgets_values"] as JArray ?? new();
        int index = 0;
        foreach (JObject input in (node["inputs"] as JArray ?? new()).OfType<JObject>())
        {
            if (input["widget"] is null)
            {
                continue;
            }
            string name = input["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException();
            }
            JToken value = (node["widgets_values_named"] as JObject)?[name] ?? values.ElementAtOrDefault(index);
            index++;
            if (value is not null)
            {
                result[name] = value.DeepClone();
            }
            if (values.ElementAtOrDefault(index)?.Type == JTokenType.String
                && values[index].ToString() is "randomize" or "fixed" or "increment" or "decrement"
                && input["type"]?.ToString() == "INT")
            {
                index++;
            }
        }
        return result;
    }

    /// <summary>Resolves linked values while checking that each link points at its declared input slot.</summary>
    private JToken Input(Scope scope, JObject node, JObject input, int slot, Dictionary<string, JToken> widgets)
    {
        string name = input["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidDataException();
        }
        JToken value = null;
        if (input["link"] is JToken linkId && linkId.Type != JTokenType.Null)
        {
            if (!scope.Links.TryGetValue(linkId.ToString(), out JArray link)
                || link[3].ToString() != node["id"].ToString() || link[4].Value<int>() != slot)
            {
                throw new InvalidDataException();
            }
            value = Output(scope, link[1].ToString(), link[2].Value<int>());
        }
        return value ?? (widgets.TryGetValue(name, out JToken fallback) ? fallback.DeepClone() : null);
    }

    /// <summary>Instantiates a definition with the caller's connected inputs or proxy-widget overrides.</summary>
    private Scope Child(Scope parent, JObject node, JObject definition)
    {
        string id = node["id"].ToString();
        if (parent.Children.TryGetValue(id, out Scope existing))
        {
            return existing;
        }
        string type = node["type"].ToString();
        if (parent.Ancestors.Count >= 8 || parent.Ancestors.Contains(type))
        {
            throw new InvalidDataException();
        }
        Scope child = CreateScope(definition, new(parent.Ancestors) { type });
        JArray inputs = node["inputs"] as JArray ?? new();
        Dictionary<string, JToken> widgets = WidgetValues(node);
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] is not JObject input)
            {
                throw new InvalidDataException();
            }
            child.Bindings.Add(Input(parent, node, input, i, widgets));
        }
        if ((definition["inputs"] as JArray)?.Count != child.Bindings.Count)
        {
            throw new InvalidDataException();
        }
        parent.Children.Add(id, child);
        return child;
    }

    /// <summary>Resolves an executable output, flattening subgraphs and rejecting cycles or unsupported node modes.</summary>
    private JToken Output(Scope scope, string id, int slot)
    {
        if (id == scope.Graph["inputNode"]?["id"]?.ToString())
        {
            if (slot >= scope.Bindings.Count)
            {
                throw new InvalidDataException();
            }
            return scope.Bindings[slot]?.DeepClone();
        }
        if (!scope.Nodes.TryGetValue(id, out JObject node) || (node.Value<int?>("mode") ?? 0) != 0
            || !scope.Visiting.Add(id))
        {
            throw new InvalidDataException();
        }
        try
        {
            string type = node["type"].ToString();
            if (Definitions.TryGetValue(type, out JObject definition))
            {
                Scope child = Child(scope, node, definition);
                string outputId = definition["outputNode"]?["id"]?.ToString();
                JArray link = child.Links.Values.SingleOrDefault(value => value[3].ToString() == outputId && value[4].Value<int>() == slot);
                if (link is null)
                {
                    throw new InvalidDataException();
                }
                return Output(child, link[1].ToString(), link[2].Value<int>());
            }
            if (type is "Note" or "MarkdownNote" || Guid.TryParse(type, out _)
                || type.Contains("LoadImage", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException();
            }
            JArray outputs = node["outputs"] as JArray ?? new();
            if (slot >= outputs.Count && type is not ("SaveImage" or "PreviewImage"))
            {
                throw new InvalidDataException();
            }
            if (!scope.Ids.TryGetValue(id, out string apiId))
            {
                if (++NextId > 2000)
                {
                    throw new InvalidDataException();
                }
                apiId = NextId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                JObject values = new();
                JArray inputs = node["inputs"] as JArray ?? new();
                Dictionary<string, JToken> widgets = WidgetValues(node);
                for (int i = 0; i < inputs.Count; i++)
                {
                    if (inputs[i] is not JObject input)
                    {
                        throw new InvalidDataException();
                    }
                    if (type == "SaveImageAdvanced" && input["name"]?.ToString() != "images")
                    {
                        continue; // The workspace owns output saving; never import administrator output paths.
                    }
                    JToken value = Input(scope, node, input, i, widgets);
                    if (value is not null)
                    {
                        values[input["name"].ToString()] = value;
                    }
                }
                Prompt[apiId] = new JObject { ["class_type"] = type == "SaveImageAdvanced" ? "PreviewImage" : type, ["inputs"] = values };
                scope.Ids.Add(id, apiId);
                // Blueprint canvas node IDs resolve into the flattened graph through this map (T118); an ID appearing in two scopes is dropped.
                if (AmbiguousCanvasIds.Add(id))
                {
                    ApiByCanvasId.TryAdd(id, apiId);
                }
                else if (ApiByCanvasId.TryGetValue(id, out string other) && other != apiId)
                {
                    ApiByCanvasId.Remove(id);
                }
            }
            return new JArray(apiId, slot);
        }
        finally
        {
            scope.Visiting.Remove(id);
        }
    }

    /// <summary>Builds only output-connected nodes, then validates and maps positive/negative text inputs.</summary>
    private JObject Build(JObject canvas)
    {
        foreach (JObject definition in (canvas?["definitions"]?["subgraphs"] as JArray ?? new()).OfType<JObject>())
        {
            if (definition["id"]?.ToString() is not string id || !Definitions.TryAdd(id, definition))
            {
                throw new InvalidDataException();
            }
        }
        Scope root = CreateScope(canvas, new());
        foreach (JObject node in root.Nodes.Values)
        {
            string type = node["type"].ToString();
            if (type is "SaveImage" or "PreviewImage" or "SaveImageAdvanced")
            {
                Output(root, node["id"].ToString(), 0);
            }
        }
        // A blueprint exposes an IMAGE slot instead of containing a save node. Add an output adapter on the server.
        if (Prompt.Count == 0)
        {
            foreach (JObject node in root.Nodes.Values.Where(node => Definitions.ContainsKey(node["type"].ToString())))
            {
                JArray outputs = node["outputs"] as JArray ?? new();
                for (int slot = 0; slot < outputs.Count; slot++)
                {
                    if (outputs[slot]?["type"]?.ToString() == "IMAGE")
                    {
                        JToken image = Output(root, node["id"].ToString(), slot) ?? throw new InvalidDataException();
                        Prompt[$"output_{NextId}"] = new JObject { ["class_type"] = "PreviewImage", ["inputs"] = new JObject { ["images"] = image } };
                    }
                }
            }
        }
        MatchRecipe();
        HashSet<(string Node, string Input)> positive = new(), negative = new();
        foreach (JProperty property in Prompt.Properties())
        {
            JObject sampler = (JObject)property.Value;
            string samplerType = sampler["class_type"]?.ToString();
            // Where the sampler takes its conditioning: a KSampler carries its positive and negative inputs directly, while the advanced
            // sampler of the official Turbo templates takes them through its guider (CFGGuider) — the same encoder check covers both.
            JObject conditioning = null;
            if (samplerType is "KSampler" or "KSamplerAdvanced")
            {
                conditioning = sampler;
            }
            else if (samplerType == "SamplerCustomAdvanced" && sampler["inputs"]?["guider"] is JArray guiderSource && guiderSource.Count == 2
                && guiderSource[1].Type == JTokenType.Integer && Prompt[guiderSource[0].ToString()] is JObject guider
                && guider["class_type"]?.ToString() == "CFGGuider")
            {
                conditioning = guider;
                GuidersOfAdvanced[guiderSource[0].ToString()] = property.Name;
            }
            if (conditioning is null)
            {
                continue;
            }
            foreach (string name in new[] { "positive", "negative" })
            {
                if (conditioning["inputs"]?[name] is not JArray source || source.Count != 2
                    || source[1].Type != JTokenType.Integer || Prompt[source[0].ToString()] is not JObject encoder)
                {
                    throw new InvalidDataException();
                }
                string field = encoder["class_type"]?.ToString() switch
                {
                    "CLIPTextEncode" when source[1].Value<int>() == 0 => "text",
                    "TextEncodeQwenImage21" when source[1].Value<int>() == (name == "positive" ? 0 : 1)
                        => name == "positive" ? "prompt" : "negative_prompt",
                    _ => null
                };
                if (field is null || encoder["inputs"]?[field]?.Type != JTokenType.String)
                {
                    throw new InvalidDataException();
                }
                if (MatchedClasses.Contains(HardwareRules.Normalize(encoder["class_type"]?.ToString() ?? "")))
                {
                    MatchedEncoders.Add(source[0].ToString());
                    MatchedSamplers.Add(property.Name);
                }
                (name == "positive" ? positive : negative).Add((source[0].ToString(), field));
            }
        }
        if (positive.Count == 0 || negative.Count == 0 || positive.Overlaps(negative))
        {
            return null;
        }
        foreach ((string nodeId, string field) in positive.Concat(negative))
        {
            bool isPositive = positive.Contains((nodeId, field));
            Bind(nodeId, field, "text", isPositive ? "创作描述" : "排除内容", isPositive ? "prompt" : "negativeprompt",
                description: !isPositive && MatchedEncoders.Contains(nodeId) ? Matched.Sampling.NegativeHint.ZhCn : null);
        }
        foreach (JProperty property in Prompt.Properties())
        {
            string id = property.Name;
            JObject node = (JObject)property.Value;
            JObject inputs = (JObject)node["inputs"];
            string type = node["class_type"].ToString();
            if (type is "KSampler" or "KSamplerAdvanced")
            {
                Bind(id, "seed", "integer", "随机种子", "seed", -1, 9007199254740991);
                Bind(id, "noise_seed", "integer", "随机种子", "seed", -1, 9007199254740991);
                Bind(id, "steps", "integer", "采样步数", "steps", 1, 100);
                // The matched recipe's cfg bounds come with their own hint text (T118); without a recipe the generic range applies.
                if (MatchedSamplers.Contains(id))
                {
                    Bind(id, "cfg", "decimal", "提示词强度", "cfgscale", Matched.Sampling.Cfg.Min, Matched.Sampling.Cfg.Max, description: Matched.Sampling.Cfg.Hint.ZhCn);
                }
                else
                {
                    Bind(id, "cfg", "decimal", "提示词强度", "cfgscale", 0, 30);
                }
                Bind(id, "sampler_name", "dropdown", "采样器", "sampler");
                Bind(id, "scheduler", "dropdown", "调度器", "scheduler");
                Bind(id, "denoise", "decimal", "降噪强度", null, 0, 1);
            }
            // The official Turbo sampling path spreads the controls over helper nodes: the seed on the noise node, the cfg on the guider
            // (with the matched recipe's bounds, as on a KSampler); the sampler itself stays on the fixed euler the sigma table is tuned for.
            if (type == "RandomNoise")
            {
                Bind(id, "noise_seed", "integer", "随机种子", "seed", -1, 9007199254740991);
            }
            if (type == "CFGGuider")
            {
                if (GuidersOfAdvanced.TryGetValue(id, out string guiderSampler) && MatchedSamplers.Contains(guiderSampler))
                {
                    Bind(id, "cfg", "decimal", "提示词强度", "cfgscale", Matched.Sampling.Cfg.Min, Matched.Sampling.Cfg.Max, description: Matched.Sampling.Cfg.Hint.ZhCn);
                }
                else
                {
                    Bind(id, "cfg", "decimal", "提示词强度", "cfgscale", 0, 30);
                }
            }
            if (type is "EmptyLatentImage" or "EmptySD3LatentImage")
            {
                // The latent follows the matched recipe's size step (T118); Bind moves a saved default that is not a multiple of it to the nearest valid value.
                int sizeStep = Matched?.SizeStep ?? 16;
                Bind(id, "width", "integer", "宽度", "width", 256, 2048, sizeStep);
                Bind(id, "height", "integer", "高度", "height", 256, 2048, sizeStep);
                Values["images"] ??= inputs["batch_size"]?.DeepClone() ?? new JValue(1);
                // SwarmUI schedules each requested image separately, avoiding images squared.
                inputs["batch_size"] = 1;
            }
            foreach (string name in new[] { "ckpt_name", "unet_name", "vae_name", "clip_name", "clip_name1", "clip_name2", "clip_name3", "lora_name" })
            {
                if (type.Contains("Loader", StringComparison.OrdinalIgnoreCase))
                {
                    Bind(id, name, "dropdown", "模型 · " + type + " · " + name, null, model: true);
                }
            }
        }
        return Parameters.Count <= 60 ? Prompt : null;
    }

    /// <summary>Only these server-created definitions and their validated values may reach the template.</summary>
    private readonly JObject Parameters = new();

    /// <summary>Saved values before placeholders replace literal node inputs.</summary>
    private readonly JObject Values = new();

    /// <summary>Converted node IDs by blueprint canvas node ID (T118), for resolving a recipe's declared access points.</summary>
    private readonly Dictionary<string, string> ApiByCanvasId = new();

    /// <summary>Canvas node IDs that appeared in more than one scope and can no longer be told apart.</summary>
    private readonly HashSet<string> AmbiguousCanvasIds = new();

    /// <summary>Encoders whose class the matched recipe declares, by prompt node ID.</summary>
    private readonly HashSet<string> MatchedEncoders = new();

    /// <summary>Samplers whose conditioning comes from an encoder the matched recipe declares, by prompt node ID.</summary>
    private readonly HashSet<string> MatchedSamplers = new();

    /// <summary>The advanced sampler each guider feeds, by guider node ID, so the guider's cfg binds to the matched recipe's bounds (T118b).</summary>
    private readonly Dictionary<string, string> GuidersOfAdvanced = new();

    /// <summary>The recipe the graph matched, chosen in <see cref="MatchRecipe"/>; null when no recipe's match conditions fit.</summary>
    private ModelRecipeRules.Recipe Matched;

    /// <summary>The matched recipe's encoder classes, normalized for lookups.</summary>
    private HashSet<string> MatchedClasses = [];

    /// <summary>The recipe book the model-specific rules come from (T118), set once at startup; null (as in the unit tests until they set it)
    /// keeps the generic ranges, hints and size step of the pre-recipe behavior.</summary>
    internal static Func<List<ModelRecipeRules.Recipe>> RecipeBook;

    /// <summary>One primary page control per semantic input; additional nodes retain independent editable fields.</summary>
    private readonly HashSet<string> FormBindings = new();

    /// <summary>Model roots the model dropdown scan reads: the working directory and the configured model root paths, set once at startup.
/// Null (as in the unit tests) narrows every model dropdown to its current value, the behavior before the scan existed.</summary>
    internal static Func<(string BaseDirectory, string[] Roots)> ModelRoots;

    /// <summary>Chooses the recipe the graph follows (T118, T118b): the main model's file name decides first — the loader's <c>unet_name</c>
    /// matched against the variants' unet files tells apart models that share an encoder class (the base model and its Turbo distillation) —
    /// and graphs without a known file fall back to the declared encoder classes. Book order decides overlaps; no book or no match leaves
    /// the generic rules in place.</summary>
    private void MatchRecipe()
    {
        List<ModelRecipeRules.Recipe> book = Book ?? RecipeBook?.Invoke();
        if (book is null || book.Count == 0)
        {
            return;
        }
        HashSet<string> classes = [.. Prompt.Properties()
            .Select(property => ((JObject)property.Value)["class_type"]?.ToString())
            .Where(type => !string.IsNullOrEmpty(type))
            .Select(HardwareRules.Normalize)];
        HashSet<string> files = [.. Prompt.Properties()
            .Select(property => ((JObject)property.Value)["inputs"]?["unet_name"])
            .Where(value => value?.Type == JTokenType.String)
            .Select(value => HardwareRules.Normalize(value.ToString()))];
        foreach (ModelRecipeRules.Recipe recipe in book)
        {
            if (recipe.Variants.Any(variant => ModelRecipeRules.FileFor(variant, ModelRecipeRules.RoleUnet) is ModelRecipeRules.FileSpec file
                && files.Contains(HardwareRules.Normalize(file.Filename))))
            {
                Matched = recipe;
                MatchedClasses = [.. recipe.Match.EncoderClasses];
                return;
            }
        }
        foreach (ModelRecipeRules.Recipe recipe in book)
        {
            if (recipe.Match.EncoderClasses.Any(match => classes.Contains(match)))
            {
                Matched = recipe;
                MatchedClasses = [.. recipe.Match.EncoderClasses];
                return;
            }
        }
    }

    /// <summary>Publishes literal inputs, following only known primitive value links without replacing graph connections.</summary>
    private void Bind(string nodeId, string inputName, string type, string label, string form, double min = 0, double max = 0, double step = 1, bool model = false, string description = null)
    {
        JObject node = (JObject)Prompt[nodeId];
        JObject inputs = (JObject)node["inputs"];
        if (inputs[inputName] is JArray link && link.Count == 2
            && Prompt[link[0].ToString()] is JObject primitive
            && primitive["class_type"]?.ToString() is "PrimitiveInt" or "PrimitiveFloat")
        {
            Bind(link[0].ToString(), "value", type, label, form, min, max, step, model, description);
            return;
        }
        if (inputs[inputName] is not JValue value || value.Type == JTokenType.Null)
        {
            return;
        }
        // Stepped integer inputs (the width and height of the empty latent, multiples of 16) keep their saved default usable: a default that is not
        // a multiple of the step moves to the nearest valid value, so a converted workflow's own size never fails every later request.
        if (type == "integer" && step > 1 && value.Type == JTokenType.Integer)
        {
            long snapped = (long)(Math.Round(value.Value<double>() / step, MidpointRounding.AwayFromZero) * step);
            value = new JValue(min < max ? Math.Clamp(snapped, (long)min, (long)max) : snapped);
        }
        // SwarmUI canonical parameter IDs retain letters only; encode digits to avoid collisions.
        string encoded = string.Concat((nodeId + "x" + inputName.Replace("_", "")).Select(character =>
            char.IsDigit(character) ? (char)('a' + character - '0') : char.ToLowerInvariant(character)));
        string id = "ecnative" + encoded;
        if (Parameters.ContainsKey(id))
        {
            return;
        }
        JObject definition = new()
        {
            ["id"] = id, ["name"] = label, ["type"] = type, ["default"] = value.DeepClone(),
            ["min"] = min, ["max"] = max, ["step"] = type == "decimal" ? 0.01 : step,
            ["native_node"] = nodeId, ["native_input"] = inputName, ["native_class"] = node["class_type"],
            ["native_model"] = model, ["native_seed"] = form == "seed", ["visible"] = true,
            ["form"] = form is not null && FormBindings.Add(form) ? form : null
        };
        if (type == "dropdown")
        {
            // Model dropdowns offer every model the server actually has (A19: server-side scan only); other dropdowns keep only their saved value.
            // Without configured roots (unit tests) this narrows to the saved value, as before the scan existed.
            (string baseDirectory, string[] roots) = ModelRoots?.Invoke() ?? (null, []);
            definition["values"] = model ? ModelFileScan.Choices(inputName, value, baseDirectory, roots) : new JArray(value.DeepClone());
        }
        if (description is not null)
        {
            definition["description"] = description;
        }
        if (inputName is "seed" or "noise_seed")
        {
            definition["description"] = "读取工作流保存的种子；填写 -1 可在每次生成时随机。";
        }
        Parameters[id] = definition;
        Values[id] = value.DeepClone();
        string tag = "${" + id + "}";
        inputs[inputName] = type is "integer" or "decimal" ? "%%_COMFYFIXME_" + tag + "_ENDFIXME_%%" : tag;
    }
}
