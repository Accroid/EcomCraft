using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Per-backend dependency checks; kept apart from the engine-side bindings so they can be unit tested without SwarmUI.</summary>
public static partial class NativeWorkflowParameters
{
    /// <summary>Finds a required or optional node input definition in a cached object_info response.</summary>
    private static JArray InputSchema(JObject schema, string type, string name) =>
        (schema?[type]?["input"]?["required"]?[name] ?? schema?[type]?["input"]?["optional"]?[name]) as JArray;

    /// <summary>Validates one backend's nodes and dropdown choices using the exact server-owned bindings.</summary>
    public static bool Supports(JObject prompt, JObject metadata, JObject values, JObject schema, out List<string> problems)
    {
        problems = new();
        if (schema is null || prompt is null)
        {
            problems.Add("节点信息尚未就绪");
            return false;
        }
        foreach (JProperty property in prompt.Properties())
        {
            string type = property.Value["class_type"].ToString();
            if (schema[type] is null)
            {
                problems.Add("缺少节点 " + type);
                continue;
            }
            JObject inputs = (JObject)property.Value["inputs"];
            foreach (JProperty input in inputs.Properties())
            {
                if (InputSchema(schema, type, input.Name)?[0] is not JArray choices || input.Value is JArray)
                {
                    continue;
                }
                // An image parameter (the standard workflows' product canvas, N10) is uploaded by the backend at generation time, so it is never in the file list.
                string raw = input.Value.Type == JTokenType.String ? input.Value.ToString() : "";
                if (raw.StartsWith("${", StringComparison.Ordinal) && raw.EndsWith('}') && metadata[raw[2..^1]]?["type"]?.ToString() == "image")
                {
                    continue;
                }
                JProperty binding = metadata.Properties().FirstOrDefault(parameter =>
                    parameter.Value["native_node"]?.ToString() == property.Name && parameter.Value["native_input"]?.ToString() == input.Name);
                string value = (binding is null ? input.Value : values[binding.Name] ?? binding.Value["default"])?.ToString();
                if (!choices.Any(choice => choice.ToString() == value))
                {
                    problems.Add($"{type} 的 {input.Name} 不可用：{value}");
                }
            }
        }
        return problems.Count == 0;
    }
}
