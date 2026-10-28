using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio;

/// <summary>Checks for workflows an account uploads for its own use (F60, decision U6), independent of SwarmUI so they can be unit tested.
/// An uploaded graph skips human review, so nodes that can run arbitrary code and inputs that look like file paths are refused.</summary>
public static class OwnWorkflowRules
{
    /// <summary>Most own workflows one account may keep.</summary>
    public const int MaxPerAccount = 20;

    /// <summary>Largest accepted workflow file, in characters of JSON.</summary>
    public const int MaxJsonLength = 2 * 1024 * 1024;

    /// <summary>Longest display name of an own workflow.</summary>
    public const int MaxNameLength = 40;

    /// <summary>Words in a node type that mark nodes able to run code or shell commands; matched as whole words of the type name.</summary>
    private static readonly HashSet<string> DangerousWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "exec", "execute", "eval", "script", "shell", "command", "cmd", "code", "subprocess"
    };

    /// <summary>The words of a node type: camel-case parts, letter runs and digit runs ("PythonExecNode" → Python, Exec, Node).</summary>
    private static readonly Regex TypeWords = new(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|\d+", RegexOptions.CultureInvariant);

    /// <summary>Values that look like a file system path outside ComfyUI's own folders: drive letters, network shares, absolute Unix paths or parent folders.</summary>
    private static readonly Regex PathLike = new(@"^\s*([A-Za-z]:[\\/]|\\\\|/[A-Za-z0-9_.~-])|(^|[\\/])\.\.([\\/]|$)", RegexOptions.CultureInvariant);

    /// <summary>One reason an uploaded workflow is refused: <c>denied_node</c> (the node type) or <c>path_value</c> (the node type and input name).</summary>
    public sealed record class Problem(string Code, string Node, string Input);

    /// <summary>Whether a JSON file is ComfyUI's API export (node IDs mapping to objects with class_type) rather than a saved canvas with "nodes".</summary>
    public static bool IsApiFormat(JObject json)
    {
        if (json is null || json["nodes"] is JArray)
        {
            return false;
        }
        List<JProperty> properties = [.. json.Properties()];
        return properties.Count > 0 && properties.All(property => property.Value is JObject node && node["class_type"] is not null);
    }

    /// <summary>Whether a node type is refused: one of its words marks code execution, or an administrator listed it.</summary>
    public static bool IsDeniedType(string type, IEnumerable<string> extraDenied)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }
        if (extraDenied is not null && extraDenied.Any(denied => string.Equals(denied?.Trim(), type.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return TypeWords.Matches(type).Any(word => DangerousWords.Contains(word.Value));
    }

    /// <summary>Whether a text input looks like a file system path (absolute, on a share, or climbing to a parent folder).</summary>
    public static bool IsPathLike(string value) => !string.IsNullOrEmpty(value) && !value.Contains("://") && PathLike.IsMatch(value);

    /// <summary>Every reason the converted graph (node ID → { class_type, inputs }) is refused, each node type or input reported once; empty when it may be used.</summary>
    public static List<Problem> Problems(JObject prompt, IEnumerable<string> extraDenied)
    {
        List<Problem> problems = [];
        if (prompt is null)
        {
            return problems;
        }
        List<string> denied = extraDenied?.ToList() ?? [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JProperty property in prompt.Properties())
        {
            if (property.Value is not JObject node)
            {
                continue;
            }
            string type = node["class_type"]?.ToString() ?? "";
            if (IsDeniedType(type, denied) && seen.Add($"node\n{type}"))
            {
                problems.Add(new Problem("denied_node", type, null));
            }
            if (node["inputs"] is not JObject inputs)
            {
                continue;
            }
            foreach (JProperty input in inputs.Properties())
            {
                if (input.Value.Type == JTokenType.String && IsPathLike(input.Value.ToString()) && seen.Add($"path\n{type}\n{input.Name}"))
                {
                    problems.Add(new Problem("path_value", type, input.Name));
                }
            }
        }
        return problems;
    }

    /// <summary>A display name from the user's text or file name: control characters removed, a trailing ".json" dropped, trimmed and limited; empty when nothing is left.</summary>
    public static string CleanName(string name)
    {
        string text = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (text.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^5].Trim();
        }
        return text.Length > MaxNameLength ? text[..MaxNameLength].Trim() : text;
    }
}
