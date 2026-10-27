using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Filters native catalog entries without changing the saved workflows or execution permissions.</summary>
public sealed class WorkflowCatalogFilter
{
    /// <summary>Dependency snapshots from available local self-starting backends only.</summary>
    private readonly List<JObject> Schemas;

    /// <summary>Fingerprints of entries already included in this catalog response.</summary>
    private readonly HashSet<string> Seen = new(StringComparer.Ordinal);

    /// <summary>Takes a request-local snapshot; never combines dependencies from different backends.</summary>
    public WorkflowCatalogFilter(IEnumerable<JObject> schemas)
    {
        Schemas = schemas.Where(schema => schema is not null).ToList();
    }

    /// <summary>Includes only complete dependency sets and distinct executable graphs with their parameter definitions and defaults.</summary>
    public bool Include(JObject prompt, JObject metadata, JObject values)
    {
        if (prompt is null || prompt.Count == 0 || metadata is null || values is null
            || !Schemas.Any(schema => NativeWorkflowParameters.Supports(prompt, metadata, values, schema, out _)))
        {
            return false;
        }
        return Seen.Add(Fingerprint(new JObject() { ["prompt"] = prompt, ["parameters"] = metadata, ["values"] = values }));
    }

    /// <summary>SHA-256 of the canonical JSON form, stable across property order.</summary>
    public static string Fingerprint(JObject content)
    {
        string canonical = Canonicalize(content).ToString(Formatting.None);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Ignores JSON property order while preserving array order, node connections and saved values.</summary>
    private static JToken Canonicalize(JToken token)
    {
        if (token is JObject obj)
        {
            return new JObject(obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => new JProperty(property.Name, Canonicalize(property.Value))));
        }
        if (token is JArray array)
        {
            return new JArray(array.Select(Canonicalize));
        }
        return token.DeepClone();
    }
}
