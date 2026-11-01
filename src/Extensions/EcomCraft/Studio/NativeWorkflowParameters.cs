using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;
using System.Globalization;

namespace EcomCraftStudio.Studio;

/// <summary>Server-owned native bindings, defaults and per-backend dependency checks.</summary>
public static partial class NativeWorkflowParameters
{
    /// <summary>Applies the engine's per-image seed increment to native bindings, preserving independent node offsets.</summary>
    public static void SynchronizeSeeds(T2IParamInput input, JObject metadata)
    {
        JProperty primary = metadata.Properties().FirstOrDefault(property =>
            property.Value.Value<bool?>("native_seed") == true && property.Value["form"]?.ToString() == "seed");
        if (primary is null || !input.TryGet(T2IParamTypes.Seed, out long seed) || seed < 0
            || !input.TryGetRaw(T2IParamType.FromNet((JObject)primary.Value), out object original))
        {
            return;
        }
        const long seedRange = 9007199254740992L;
        long offset = seed % seedRange - Convert.ToInt64(original, CultureInfo.InvariantCulture);
        foreach (JProperty property in metadata.Properties())
        {
            if (property.Value.Value<bool?>("native_seed") != true)
            {
                continue;
            }
            T2IParamType type = T2IParamType.FromNet((JObject)property.Value);
            if (input.TryGetRaw(type, out object value))
            {
                long updated = ((Convert.ToInt64(value, CultureInfo.InvariantCulture) + offset) % seedRange + seedRange) % seedRange;
                input.Set(type, updated.ToString(CultureInfo.InvariantCulture));
            }
        }
        input.Set(T2IParamTypes.Seed, seed % seedRange);
    }

    /// <summary>Adds locally advertised options while retaining saved names, including missing models.</summary>
    public static void AddChoices(JObject metadata, IEnumerable<JObject> schemas)
    {
        foreach (JObject definition in metadata.Properties().Select(property => property.Value).OfType<JObject>())
        {
            if (definition["type"]?.ToString() != "dropdown")
            {
                continue;
            }
            List<string> choices = new() { definition["default"].ToString() };
            foreach (JObject schema in schemas)
            {
                if (InputSchema(schema, definition["native_class"].ToString(), definition["native_input"].ToString())?[0] is JArray options)
                {
                    choices.AddRange(options.Values<string>());
                }
            }
            definition["values"] = new JArray(choices.Distinct(StringComparer.Ordinal));
        }
    }

    /// <summary>Returns only displayable defaults; no raw graph, source paths or private images. Hidden parameters are left out, so the page hides their fields.</summary>
    public static JObject FormDefaults(JObject metadata, JObject values)
    {
        JObject defaults = new() { ["images"] = values["images"]?.DeepClone() ?? new JValue(1) };
        foreach (JProperty property in metadata.Properties())
        {
            string form = property.Value["form"]?.ToString();
            if (!string.IsNullOrEmpty(form) && property.Value.Value<bool?>("visible") != false)
            {
                defaults[form] = values[property.Name]?.DeepClone() ?? property.Value["default"]?.DeepClone();
            }
        }
        return defaults;
    }

    /// <summary>Dropdown options any of the given servers currently offers for a bound input (R4.3), or null when the binding has no native input to look up.</summary>
    public static HashSet<string> OfferedChoices(JObject definition, IEnumerable<JObject> schemas)
    {
        string type = definition?["native_class"]?.ToString();
        string name = definition?["native_input"]?.ToString();
        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(name))
        {
            return null;
        }
        HashSet<string> offered = new(StringComparer.Ordinal);
        foreach (JObject schema in schemas)
        {
            if (InputSchema(schema, type, name)?[0] is JArray options)
            {
                offered.UnionWith(options.Values<string>());
            }
        }
        return offered;
    }
}
