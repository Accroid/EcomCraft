using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EcomCraftStudio.Studio.Templates;

/// <summary>A model a template needs (F08): file name, the model folder it is installed in, its commercial-use license status and links.</summary>
public sealed record class ModelRequirement(string Name, string Folder, string Commercial, string LicenseUrl, string DownloadUrl);

/// <summary>Model and node requirements of templates, independent of SwarmUI so validation and the missing-item check can be unit tested.
/// Models are installed by administrators by hand on each server (assumption A2); nothing is downloaded across servers.</summary>
public static class RequirementRules
{
    /// <summary>Allowed commercial-use states.</summary>
    public static readonly HashSet<string> CommercialStates = ["allowed", "forbidden", "unknown"];

    /// <summary>Model folders an administrator installs into, as named under ComfyUI's models directory.</summary>
    public static readonly HashSet<string> Folders = ["checkpoints", "diffusion_models", "unet", "loras", "vae", "text_encoders", "clip", "clip_vision", "controlnet", "upscale_models", "embeddings", "ipadapter", "other"];

    /// <summary>Reads the "requirements" object: models [{ name, folder, commercial, license_url?, download_url? }] and nodes [class names]. Problems are added to errors.</summary>
    public static (List<ModelRequirement> Models, List<string> Nodes) Parse(JToken token, List<string> errors)
    {
        List<ModelRequirement> models = [];
        List<string> nodes = [];
        if (token is null || token.Type == JTokenType.Null)
        {
            return (models, nodes);
        }
        if (token is not JObject requirements || requirements.Properties().Any(property => property.Name is not ("models" or "nodes")))
        {
            errors.Add("requirements 只能包含 models 和 nodes。");
            return (models, nodes);
        }
        foreach (JToken item in requirements["models"] as JArray ?? new JArray())
        {
            string name = item?["name"]?.ToString()?.Trim() ?? "";
            string folder = item?["folder"]?.ToString()?.Trim() ?? "";
            string commercial = item?["commercial"]?.ToString()?.Trim() ?? "unknown";
            string license = item?["license_url"]?.ToString()?.Trim();
            string download = item?["download_url"]?.ToString()?.Trim();
            if (name.Length == 0 || name.Length > 200 || name.Contains("..") || name.StartsWith('/') || name.Contains('\\'))
            {
                errors.Add($"requirements.models 中的模型名称“{name}”无效。");
                continue;
            }
            if (!Folders.Contains(folder))
            {
                errors.Add($"模型“{name}”的 folder 须为：{string.Join("、", Folders)}。");
                continue;
            }
            if (!CommercialStates.Contains(commercial))
            {
                errors.Add($"模型“{name}”的 commercial 须为 allowed、forbidden 或 unknown。");
                continue;
            }
            if (!IsHttpUrl(license) || !IsHttpUrl(download))
            {
                errors.Add($"模型“{name}”的 license_url / download_url 须为 http 或 https 链接。");
                continue;
            }
            models.Add(new ModelRequirement(name, folder, commercial, string.IsNullOrEmpty(license) ? null : license, string.IsNullOrEmpty(download) ? null : download));
        }
        foreach (JToken item in requirements["nodes"] as JArray ?? new JArray())
        {
            string node = item?.ToString()?.Trim() ?? "";
            if (!Regex.IsMatch(node, @"^[\w .()+\-]{1,120}$"))
            {
                errors.Add($"requirements.nodes 中的节点名“{node}”无效。");
                continue;
            }
            nodes.Add(node);
        }
        return (models, nodes);
    }

    /// <summary>Whether a link is empty or an absolute http(s) URL.</summary>
    private static bool IsHttpUrl(string url) => string.IsNullOrEmpty(url)
        || (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && uri.Scheme is "http" or "https");

    /// <summary>Every model file name a server's ComfyUI offers, read from all list-valued inputs of its object_info (loaders list their files there).</summary>
    public static HashSet<string> OfferedModels(JObject objectInfo)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (JProperty node in objectInfo?.Properties() ?? [])
        {
            foreach (string section in new[] { "required", "optional" })
            {
                foreach (JProperty input in (node.Value["input"]?[section] as JObject)?.Properties() ?? [])
                {
                    if (input.Value is JArray definition && definition.First is JArray options)
                    {
                        foreach (JToken option in options)
                        {
                            if (option.Type == JTokenType.String)
                            {
                                string value = option.ToString().Replace('\\', '/');
                                names.Add(value);
                                names.Add(value[(value.LastIndexOf('/') + 1)..]);
                            }
                        }
                    }
                }
            }
        }
        return names;
    }

    /// <summary>The models a server lacks, matching by file name with or without a subfolder.</summary>
    public static List<ModelRequirement> MissingModels(IEnumerable<ModelRequirement> models, HashSet<string> offered) =>
        [.. models.Where(model => !offered.Contains(model.Name) && !offered.Contains(model.Name.Replace('\\', '/')[(model.Name.Replace('\\', '/').LastIndexOf('/') + 1)..]))];

    /// <summary>The nodes a server lacks.</summary>
    public static List<string> MissingNodes(IEnumerable<string> nodes, ICollection<string> nodeTypes) => [.. nodes.Where(node => !nodeTypes.Contains(node))];
}
