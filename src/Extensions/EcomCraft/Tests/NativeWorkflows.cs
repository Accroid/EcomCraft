using EcomCraftStudio.Studio;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;
using SwarmUI.Builtin_ComfyUIBackend;

/// <summary>GPU-free regression checks for native canvases, subgraphs and filesystem discovery.</summary>
internal static class NativeWorkflows
{
    /// <summary>Checks an invariant without printing user workflow content.</summary>
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    /// <summary>Minimal executable graph with linked widget values and frontend seed controls.</summary>
    private static JObject Canvas() => JObject.Parse("""
        {"nodes":[
          {"id":1,"type":"CLIPTextEncode","inputs":[{"name":"text","type":"STRING","widget":{}}],"outputs":[{}],"widgets_values":["positive"]},
          {"id":2,"type":"CLIPTextEncode","inputs":[{"name":"text","type":"STRING","widget":{}}],"outputs":[{}],"widgets_values":["negative"]},
          {"id":3,"type":"PrimitiveInt","inputs":[{"name":"value","type":"INT","widget":{}}],"outputs":[{}],"widgets_values":[20,"fixed"]},
          {"id":4,"type":"KSampler","inputs":[
            {"name":"positive","link":1},{"name":"negative","link":2},
            {"name":"seed","type":"INT","widget":{}},
            {"name":"steps","type":"INT","widget":{},"link":3},
            {"name":"cfg","type":"FLOAT","widget":{}},
            {"name":"sampler_name","type":"COMBO","widget":{}},
            {"name":"scheduler","type":"COMBO","widget":{}},
            {"name":"denoise","type":"FLOAT","widget":{}}
          ],"outputs":[{}],"widgets_values":[42,"randomize",8,4,"euler","simple",0.75]},
          {"id":5,"type":"PreviewImage","inputs":[{"name":"images","link":4}],"outputs":[]}
        ],"links":[[1,1,0,4,0],[2,2,0,4,1],[3,3,0,4,3],[4,4,0,5,0]]}
        """);

    /// <summary>Builds an embedded blueprint with virtual input/output nodes and no root save node.</summary>
    private static JObject Blueprint()
    {
        JObject definition = Canvas();
        definition["id"] = "blueprint";
        definition["inputNode"] = new JObject { ["id"] = -10 };
        definition["outputNode"] = new JObject { ["id"] = -20 };
        definition["inputs"] = new JArray(new JObject { ["name"] = "text" });
        definition["outputs"] = new JArray(new JObject { ["type"] = "IMAGE" });
        JArray nodes = (JArray)definition["nodes"];
        nodes.Last.Remove();
        nodes[0]["inputs"][0]["link"] = 5;
        JArray links = (JArray)definition["links"];
        links[3][3] = -20;
        links.Add(new JArray(5, -10, 0, 1, 0));
        definition["links"] = new JArray(links.Cast<JArray>().Select(link => new JObject
        {
            ["id"] = link[0], ["origin_id"] = link[1], ["origin_slot"] = link[2], ["target_id"] = link[3], ["target_slot"] = link[4]
        }));
        return new JObject
        {
            ["nodes"] = new JArray(new JObject
            {
                ["id"] = 10, ["type"] = "blueprint", ["widgets_values"] = new JArray(),
                ["inputs"] = new JArray(new JObject { ["name"] = "text", ["type"] = "STRING", ["widget"] = new JObject() }),
                ["outputs"] = new JArray(new JObject { ["type"] = "IMAGE" })
            }),
            ["links"] = new JArray(), ["definitions"] = new JObject { ["subgraphs"] = new JArray(definition) }
        };
    }

    /// <summary>Asserts prompt mapping, linked-control preservation, and widget alignment.</summary>
    private static void CheckPrompt(JObject canvas)
    {
        JObject template = NativeWorkflowConverter.Convert(canvas, out JObject metadata, out JObject values);
        JObject prompt = Render(template, metadata, values);
        CheckMappedPrompt(prompt);
    }

    /// <summary>Checks linked widgets after the actual SwarmUI template filler runs.</summary>
    private static void CheckMappedPrompt(JObject prompt)
    {
        Check(prompt is not null, "Graph should import");
        JObject sampler = prompt.Properties().Select(property => property.Value).OfType<JObject>().Single(node => node["class_type"]?.ToString() == "KSampler");
        Check(sampler["inputs"]["steps"] is JArray, "Linked step control must be preserved");
        Check(sampler["inputs"]["denoise"].Value<double>() == 0.75, "Linked widgets must still consume serialized positions");
        JArray positive = (JArray)sampler["inputs"]["positive"];
        JArray negative = (JArray)sampler["inputs"]["negative"];
        Check(prompt[positive[0].ToString()]["inputs"]["text"].ToString() == "positive", "Positive prompt must map");
        Check(prompt[negative[0].ToString()]["inputs"]["text"].ToString() == "negative", "Negative prompt must map");
    }

    /// <summary>Runs the production template filler without a server, database, network or GPU.</summary>
    private static JObject Render(JObject template, JObject metadata, JObject values)
    {
        T2IParamInput input = new(null);
        input.Set(ComfyUIBackendExtension.CustomWorkflowParam, "PARSED%test%" + template.ToString(Newtonsoft.Json.Formatting.None));
        Func<string, T2IParamInput, T2IParamType> provider = (name, _) => metadata[name] is JObject definition ? T2IParamType.FromNet(definition) : null;
        T2IParamTypes.FakeTypeProviders.Add(provider);
        try
        {
            foreach (JProperty property in metadata.Properties())
            {
                input.Set(T2IParamType.FromNet((JObject)property.Value), values[property.Name].ToString());
            }
            return JObject.Parse(ComfyUIAPIAbstractBackend.CreateWorkflow(input, workflow => workflow));
        }
        finally
        {
            T2IParamTypes.FakeTypeProviders.Remove(provider);
        }
    }

    /// <summary>Checks four independent image payloads using the engine's clone/increment sequence and production filler.</summary>
    private static void CheckBatchSeeds(JObject template, JObject metadata, JObject values)
    {
        JProperty[] seeds = metadata.Properties().Where(property => property.Value.Value<bool?>("native_seed") == true).ToArray();
        Check(seeds.Length > 0, "Batch fixture must have native seed bindings");
        Func<string, T2IParamInput, T2IParamType> provider = (name, _) => metadata[name] is JObject definition ? T2IParamType.FromNet(definition) : null;
        T2IParamTypes.FakeTypeProviders.Add(provider);
        try
        {
            foreach (long baseSeed in new long[] { 1095546475779037, 9007199254740991 })
            {
                T2IParamInput original = new(null);
                original.Set(ComfyUIBackendExtension.CustomWorkflowParam, "PARSED%test%" + template.ToString(Newtonsoft.Json.Formatting.None));
                original.Set(T2IParamTypes.Seed, baseSeed);
                foreach (JProperty property in metadata.Properties())
                {
                    original.Set(T2IParamType.FromNet((JObject)property.Value), values[property.Name].ToString());
                }
                foreach (JProperty property in seeds)
                {
                    // Different samplers keep their independent seeds, including wraparound in either direction.
                    long initial = property.Value["form"]?.ToString() == "seed" ? baseSeed : 9007199254740990;
                    original.Set(T2IParamType.FromNet((JObject)property.Value), initial.ToString());
                }
                HashSet<string> payloads = new();
                for (int index = 0; index < 4; index++)
                {
                    T2IParamInput image = original.Clone();
                    image.Set(T2IParamTypes.Seed, original.Get(T2IParamTypes.Seed) + index);
                    NativeWorkflowParameters.SynchronizeSeeds(image, metadata);
                    NativeWorkflowParameters.SynchronizeSeeds(image, metadata); // Re-entry must not increment twice.
                    JObject rendered = JObject.Parse(ComfyUIAPIAbstractBackend.CreateWorkflow(image, workflow => workflow));
                    Check(payloads.Add(rendered.ToString()), "Every image must send a distinct executable payload");
                    foreach (JProperty property in metadata.Properties())
                    {
                        JObject definition = (JObject)property.Value;
                        JToken actual = rendered[definition["native_node"].ToString()]["inputs"][definition["native_input"].ToString()];
                        if (definition.Value<bool?>("native_seed") == true)
                        {
                            long initial = definition["form"]?.ToString() == "seed" ? baseSeed : 9007199254740990;
                            long expected = (initial + index) % 9007199254740992L;
                            Check(actual.Value<long>() == expected, "Per-image increment must reach every native seed binding");
                            T2IParamType type = T2IParamType.FromNet(definition);
                            Check(image.TryGetRaw(type, out object current) && Convert.ToInt64(current) == expected,
                                "Dynamic seed metadata must agree with the actual payload");
                            Check(original.TryGetRaw(type, out object saved) && Convert.ToInt64(saved) == initial,
                                "Image clones must not mutate the original request");
                            if (definition["form"]?.ToString() == "seed")
                            {
                                Check(image.Get(T2IParamTypes.Seed) == expected, "Root metadata must agree with the primary sampler seed");
                            }
                        }
                        else
                        {
                            Check(JToken.DeepEquals(actual, values[property.Name]), "Seed synchronization must preserve other inputs");
                        }
                    }
                }
            }
        }
        finally
        {
            T2IParamTypes.FakeTypeProviders.Remove(provider);
        }
        Console.WriteLine("Four-image payload seeds, independent samplers, large seeds, wraparound and clone isolation passed.");
    }

    /// <summary>Checks real saved values and user edits at the final ComfyUI payload boundary.</summary>
    private static void CheckRealParameters(JObject canvas, JObject template, JObject metadata, JObject values)
    {
        JObject rendered = Render(template, metadata, values);
        foreach (JProperty property in metadata.Properties())
        {
            JObject definition = (JObject)property.Value;
            JToken actual = rendered[definition["native_node"].ToString()]["inputs"][definition["native_input"].ToString()];
            Check(JToken.DeepEquals(actual, values[property.Name]), "Saved defaults must survive actual workflow filling");
        }
        JObject defaults = NativeWorkflowParameters.FormDefaults(metadata, values);
        JObject qwen = template.Properties().Select(property => property.Value).OfType<JObject>()
            .FirstOrDefault(node => node["class_type"]?.ToString() == "TextEncodeQwenImage21");
        if (qwen is not null)
        {
            Check(template.Properties().Any(property => property.Value["class_type"]?.ToString() == "UnetLoaderGGUF"),
                "Qwen GGUF loader must remain connected");
            Check(!template.Properties().Any(property => property.Value["class_type"]?.ToString() == "UNETLoader"),
                "Disconnected bypassed regular loader must not execute");
            Check(template.Properties().Any(property => property.Value["class_type"]?.ToString() == "ResolutionSelector"),
                "Connected resolution computation must remain intact");
            Check(!template.ToString().Contains("filename_prefix"), "Administrator output filename must not be imported");
            JObject edits = (JObject)values.DeepClone();
            foreach (JProperty property in metadata.Properties())
            {
                string form = property.Value["form"]?.ToString();
                if (form is "prompt" or "negativeprompt")
                {
                    edits[property.Name] = form == "prompt" ? "edited Qwen prompt" : "edited Qwen negative";
                }
            }
            JObject result = Render(template, metadata, edits);
            JObject encoded = result.Properties().Select(property => property.Value).OfType<JObject>()
                .Single(node => node["class_type"]?.ToString() == "TextEncodeQwenImage21");
            Check(encoded["inputs"]["prompt"].ToString() == "edited Qwen prompt"
                && encoded["inputs"]["negative_prompt"].ToString() == "edited Qwen negative",
                "Positive and negative text on one Qwen encoder must map independently");
            JObject malformed = (JObject)canvas.DeepClone();
            JObject graph = (JObject)malformed["definitions"]["subgraphs"][0];
            JObject negativeLink = ((JArray)graph["links"]).OfType<JObject>().Single(link => link["id"].Value<int>() == 655);
            negativeLink["origin_slot"] = 0;
            Check(NativeWorkflowConverter.Convert(malformed) is null, "Swapped Qwen conditioning outputs must be rejected");
            Console.WriteLine("Qwen 2.1 GGUF graph, separate prompts, output adapter and linked resolution checks passed.");
        }

        if ((canvas["nodes"] as JArray).OfType<JObject>().Any(node => node["type"]?.ToString() == "CheckpointLoaderSimple"))
        {
            Check(defaults.Value<int>("width") == 512 && defaults.Value<int>("height") == 512, "Test workflow size must be read");
            Check(defaults.Value<int>("steps") == 20 && defaults.Value<double>("cfgscale") == 8, "Test workflow sampling defaults must be read");
            Check(defaults.Value<long>("seed") == 48507568374478, "Seeds larger than uint32 must remain exact");
            JObject edits = new() { ["prompt"] = "edited \"quoted\" prompt\n第二行", ["negativeprompt"] = "edited negative", ["width"] = 768, ["height"] = 640,
                ["steps"] = 27, ["cfgscale"] = 6.5, ["seed"] = 48507568374479, ["sampler"] = "heun", ["scheduler"] = "karras" };
            JObject edited = (JObject)values.DeepClone();
            foreach (JProperty property in metadata.Properties())
            {
                JObject definition = (JObject)property.Value;
                string form = definition["form"]?.ToString();
                if (!string.IsNullOrEmpty(form) && edits[form] is JToken value)
                {
                    edited[property.Name] = value.DeepClone();
                }
                if (definition["native_input"]?.ToString() == "ckpt_name")
                {
                    Check(values[property.Name].ToString() == "sd-v1-5-inpainting.ckpt", "Saved checkpoint filename must survive import");
                    edited[property.Name] = "edited-model.safetensors";
                }
                if (definition["native_input"]?.ToString() == "denoise")
                {
                    edited[property.Name] = 0.65;
                }
            }
            JObject result = Render(template, metadata, edited);
            JObject schema = new();
            foreach (JObject node in result.Properties().Select(property => property.Value).OfType<JObject>())
            {
                schema[node["class_type"].ToString()] = new JObject { ["input"] = new JObject { ["required"] = new JObject() } };
            }
            schema["CheckpointLoaderSimple"]["input"]["required"]["ckpt_name"] = new JArray { new JArray("edited-model.safetensors") };
            Check(NativeWorkflowParameters.Supports(template, metadata, edited, schema, out _), "Edited model must be checked against the selected backend");
            Check(!NativeWorkflowParameters.Supports(template, metadata, values, schema, out List<string> problems)
                && problems.Any(problem => problem.Contains("sd-v1-5-inpainting.ckpt")), "Missing saved checkpoint must produce a specific dependency error");
            schema.Remove("KSampler");
            Check(!NativeWorkflowParameters.Supports(template, metadata, edited, schema, out _), "A backend missing required nodes must not be selected");
            foreach (JProperty property in metadata.Properties())
            {
                JObject definition = (JObject)property.Value;
                JToken actual = result[definition["native_node"].ToString()]["inputs"][definition["native_input"].ToString()];
                Check(JToken.DeepEquals(actual, edited[property.Name]), "Every edit must reach its exact ComfyUI node input");
            }
            Console.WriteLine("Real workflow defaults and edited model, prompts, dimensions, seed, steps, CFG, sampler, scheduler and denoise passed the production template filler.");
        }
    }

    /// <summary>Checks catalog filtering against missing dependencies, duplicate JSON and distinct saved configurations.</summary>
    private static void CheckCatalogFilter()
    {
        JObject prompt = JObject.Parse("""{"1":{"class_type":"Loader","inputs":{"model":"${model}"}},"2":{"class_type":"SaveImage","inputs":{"images":["1",0]}}}""");
        JObject metadata = JObject.Parse("""{"model":{"native_node":"1","native_input":"model","default":"installed"}}""");
        JObject values = new() { ["model"] = "installed", ["prompt"] = "" };
        JObject schema = JObject.Parse("""{"Loader":{"input":{"required":{"model":[["installed","alternative"]]}}},"SaveImage":{"input":{"required":{"images":["IMAGE"]}}}}""");
        WorkflowCatalogFilter filter = new([schema]);
        Check(filter.Include(prompt, metadata, values), "Blank prompt text must not hide a complete workflow");
        JObject reordered = JObject.Parse("""{"2":{"inputs":{"images":["1",0]},"class_type":"SaveImage"},"1":{"inputs":{"model":"${model}"},"class_type":"Loader"}}""");
        Check(!filter.Include(reordered, metadata, values), "Duplicate executable content must collapse regardless of JSON property order");
        JObject edited = (JObject)values.DeepClone();
        edited["model"] = "alternative";
        Check(filter.Include(prompt, metadata, edited), "Different saved models must remain separate choices");
        JObject differentGraph = (JObject)prompt.DeepClone();
        differentGraph["2"]["inputs"]["images"][1] = 1;
        Check(filter.Include(differentGraph, metadata, values), "Different hidden node connections must not be merged by matching form fields");
        edited["model"] = "missing";
        Check(!filter.Include(prompt, metadata, edited), "Missing saved models must be hidden");
        Check(!filter.Include(new JObject(), metadata, values), "Empty graphs must be hidden");
        Check(!new WorkflowCatalogFilter([]).Include(prompt, metadata, values), "Unavailable local backends must not produce choices");
        JObject loaderOnly = (JObject)schema.DeepClone();
        loaderOnly.Remove("SaveImage");
        JObject outputOnly = (JObject)schema.DeepClone();
        outputOnly.Remove("Loader");
        Check(!new WorkflowCatalogFilter([loaderOnly, outputOnly]).Include(prompt, metadata, values),
            "Dependencies split across two backends must not be treated as one complete backend");
        Check(new WorkflowCatalogFilter([loaderOnly, schema]).Include(prompt, metadata, values),
            "A complete second backend must still make the workflow available");
        Console.WriteLine("Catalog dependency filtering and executable-content deduplication checks passed.");
    }

    /// <summary>Runs synthetic checks, and optionally validates a real workflow against local node schemas.</summary>
    public static async Task Main(string[] args)
    {
        T2IParamTypes.RegisterDefaults();
        CheckCatalogFilter();
        ComfyUIBackendExtension.CustomWorkflowParam = T2IParamTypes.Register<string>(new("Test workflow", "", "", ID: "comfyuicustomworkflow"));
        CheckPrompt(Canvas());
        JObject blueprint = Blueprint();
        CheckPrompt(blueprint);
        JObject duplicate = (JObject)blueprint.DeepClone();
        JObject second = (JObject)duplicate["nodes"][0].DeepClone();
        second["id"] = 11;
        ((JArray)duplicate["nodes"]).Add(second);
        JObject doubled = NativeWorkflowConverter.Convert(duplicate, out JObject doubledMetadata, out JObject doubledValues);
        Check(doubledMetadata.Properties().All(property => property.Name == T2IParamTypes.CleanTypeName(property.Name)),
            "Dynamic IDs must survive SwarmUI normalization without losing node identity");
        Check(Render(doubled, doubledMetadata, doubledValues).Count == doubled.Count, "Independent subgraph parameters must all resolve");
        Check(doubled is not null && doubled.Properties().Count(property => property.Value["class_type"]?.ToString() == "KSampler") == 2,
            "Multiple instances must have distinct executable nodes");
        CheckBatchSeeds(doubled, doubledMetadata, doubledValues);
        JObject linkedSeed = Canvas();
        linkedSeed["nodes"][3]["inputs"][2]["link"] = 3;
        ((JObject)linkedSeed["nodes"][3]["inputs"][3]).Remove("link");
        linkedSeed["links"][2][4] = 2;
        JObject linkedTemplate = NativeWorkflowConverter.Convert(linkedSeed, out JObject linkedMetadata, out JObject linkedValues);
        CheckBatchSeeds(linkedTemplate, linkedMetadata, linkedValues);
        JObject bad = Canvas();
        bad["nodes"][3]["inputs"][1]["link"] = 999;
        Check(NativeWorkflowConverter.Convert(bad) is null, "Dangling links must be rejected");
        bad = Canvas();
        bad["links"][1][1] = 1;
        Check(NativeWorkflowConverter.Convert(bad) is null, "Conflicting positive and negative prompts must be rejected");
        bad = Canvas();
        bad["nodes"][3]["mode"] = 4;
        Check(NativeWorkflowConverter.Convert(bad) is null, "Unsupported bypass modes must not silently execute");
        bad = Blueprint();
        bad["definitions"]["subgraphs"][0]["nodes"][0]["type"] = "blueprint";
        Check(NativeWorkflowConverter.Convert(bad) is null, "Recursive definitions must be rejected");
        bad = Canvas();
        bad["links"][2][1] = 4;
        Check(NativeWorkflowConverter.Convert(bad) is null, "Graph cycles must be rejected");
        bad = Canvas();
        bad["nodes"][2]["type"] = "LoadImage";
        Check(NativeWorkflowConverter.Convert(bad) is null, "Administrator default images must not be imported");
        string root = Path.Combine(Path.GetTempPath(), "ecomcraft-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            string configured = Path.Combine(root, "manual");
            string primary = Path.Combine(root, "ComfyUI");
            string sibling = Path.Combine(root, "test", "ComfyUI");
            string redirected = Path.Combine(primary, "custom user", "default", "workflows");
            string blueprints = Path.Combine(sibling, "blueprints");
            foreach (string path in new[] { configured, redirected, blueprints, Path.Combine(primary, "workflows") })
            {
                Directory.CreateDirectory(path);
            }
            File.WriteAllText(Path.Combine(sibling, "main.py"), "");
            File.WriteAllText(Path.Combine(sibling, "folder_paths.py"), "");
            Check(NativeWorkflowDiscovery.SourceLabel(blueprints) == "test", "Duplicate names should identify the installation without exposing a full path");
            List<string> paths = NativeWorkflowDiscovery.Collect([(primary, "--user-directory \"custom user\"")], [configured], [primary, sibling]);
            Check(paths.Contains(configured) && paths.Contains(redirected) && paths.Contains(blueprints), "Configured directories must supplement automatic discovery");
            Check(paths.Count == paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Folders must be deduplicated");
        }
        finally
        {
            Directory.Delete(root, true); // Only the unique disposable test directory created above.
        }
        if (args.Length > 0)
        {
            JObject canvas = JObject.Parse(File.ReadAllText(args[0]));
            JObject real = NativeWorkflowConverter.Convert(canvas, out JObject metadata, out JObject values);
            Check(real is not null, "The supplied workflow must import");
            CheckRealParameters(canvas, real, metadata, values);
            CheckBatchSeeds(real, metadata, values);
            if (args.Length > 1)
            {
                using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
                JObject info = JObject.Parse(await client.GetStringAsync(args[1]));
                if (args.Length > 2 && args[2] == "--require-dependencies")
                {
                    Check(NativeWorkflowParameters.Supports(real, metadata, values, info, out _),
                        "The running backend must advertise every saved workflow dependency");
                    Console.WriteLine("All saved workflow node and model dependencies are available on the running local backend.");
                }
                foreach (JProperty property in real.Properties())
                {
                    JObject node = (JObject)property.Value;
                    JObject schema = info[node["class_type"].ToString()] as JObject;
                    Check(schema is not null, "A converted node is unavailable in the local backend");
                    foreach (JProperty required in (schema["input"]?["required"] as JObject ?? new()).Properties())
                    {
                        // V3 autogrow containers expand into individual live inputs; min=0 permits none.
                        bool emptyAutogrow = required.Value is JArray inputSchema
                            && inputSchema[0]?.ToString() == "COMFY_AUTOGROW_V3"
                            && inputSchema.ElementAtOrDefault(1)?["template"]?["min"]?.Value<int>() == 0;
                        Check(emptyAutogrow || node["inputs"]?[required.Name] is not null, "A converted node is missing a required input");
                    }
                }
            }
            Console.WriteLine($"Actual workflow converted and checked: {real.Count} executable nodes.");
        }
        Console.WriteLine("Native workflow conversion and discovery checks passed.");
    }
}
