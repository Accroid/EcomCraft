using Newtonsoft.Json.Linq;
using System.IO;

namespace EcomCraftStudio.Studio;

/// <summary>Converts saved canvases and embedded ComfyUI subgraphs into validated server-owned prompts.</summary>
public sealed class NativeWorkflowConverter
{
    /// <summary>Embedded definitions are resolved only from the saved file, never from client input or external files.</summary>
    private readonly Dictionary<string, JObject> Definitions = new();

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
        NativeWorkflowConverter converter = new();
        metadata = converter.Parameters;
        values = converter.Values;
        try
        {
            return converter.Build(canvas);
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
        HashSet<(string Node, string Input)> positive = new(), negative = new();
        foreach (JObject node in Prompt.Properties().Select(property => property.Value).OfType<JObject>())
        {
            if (node["class_type"]?.ToString() is "KSampler" or "KSamplerAdvanced")
            {
                foreach (string name in new[] { "positive", "negative" })
                {
                    if (node["inputs"]?[name] is not JArray source || source.Count != 2
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
                    (name == "positive" ? positive : negative).Add((source[0].ToString(), field));
                }
            }
        }
        if (positive.Count == 0 || negative.Count == 0 || positive.Overlaps(negative))
        {
            return null;
        }
        foreach ((string nodeId, string field) in positive.Concat(negative))
        {
            bool isPositive = positive.Contains((nodeId, field));
            Bind(nodeId, field, "text", isPositive ? "创作描述" : "排除内容", isPositive ? "prompt" : "negativeprompt");
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
                Bind(id, "cfg", "decimal", "提示词强度", "cfgscale", 0, 30);
                Bind(id, "sampler_name", "dropdown", "采样器", "sampler");
                Bind(id, "scheduler", "dropdown", "调度器", "scheduler");
                Bind(id, "denoise", "decimal", "降噪强度", null, 0, 1);
            }
            if (type is "EmptyLatentImage" or "EmptySD3LatentImage")
            {
                Bind(id, "width", "integer", "宽度", "width", 256, 2048, 8);
                Bind(id, "height", "integer", "高度", "height", 256, 2048, 8);
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

    /// <summary>One primary page control per semantic input; additional nodes retain independent editable fields.</summary>
    private readonly HashSet<string> FormBindings = new();

    /// <summary>Publishes literal inputs, following only known primitive value links without replacing graph connections.</summary>
    private void Bind(string nodeId, string inputName, string type, string label, string form, double min = 0, double max = 0, double step = 1, bool model = false)
    {
        JObject node = (JObject)Prompt[nodeId];
        JObject inputs = (JObject)node["inputs"];
        if (inputs[inputName] is JArray link && link.Count == 2
            && Prompt[link[0].ToString()] is JObject primitive
            && primitive["class_type"]?.ToString() is "PrimitiveInt" or "PrimitiveFloat")
        {
            Bind(link[0].ToString(), "value", type, label, form, min, max, step, model);
            return;
        }
        if (inputs[inputName] is not JValue value || value.Type == JTokenType.Null)
        {
            return;
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
            definition["values"] = new JArray(value.DeepClone());
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
