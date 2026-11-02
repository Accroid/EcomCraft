using Newtonsoft.Json.Linq;
using System.IO;

namespace EcomCraftStudio.Studio;

/// <summary>Model folder scanning for the model dropdowns (A19: server-side scan only, never client-provided names). Pure logic with no SwarmUI
/// dependency, so the unit tests build it on its own: callers pass the model roots, and a null or empty root list narrows every dropdown to its
/// current value, the same as before the scan existed.</summary>
public static class ModelFileScan
{
    /// <summary>Model folders of each loader input, relative to every model root, in ComfyUI's layout.</summary>
    public static readonly Dictionary<string, string[]> Folders = new()
    {
        ["unet_name"] = ["unet", "diffusion_models"],
        ["ckpt_name"] = ["Stable-Diffusion", "diffusion_models", "unet"],
        ["clip_name"] = ["text_encoders", "clip"],
        ["vae_name"] = ["VAE"]
    };

    /// <summary>Model file names visible to the platform in the folders of one loader input: safetensors and GGUF only (A22), subfolder paths with
    /// forward slashes as ComfyUI shows them. Empty when the input is not a known model input or no folder can be read.</summary>
    /// <param name="input">The loader input name, such as "unet_name".</param>
    /// <param name="baseDirectory">The directory relative model roots resolve against.</param>
    /// <param name="roots">The model roots, semicolon-configured entries already split; null or empty scans nothing.</param>
    public static List<string> List(string input, string baseDirectory, IEnumerable<string> roots)
    {
        if (!Folders.TryGetValue(input, out string[] folders))
        {
            return [];
        }
        SortedSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots ?? [])
        {
            foreach (string folder in folders)
            {
                string path = Combine(baseDirectory, root, folder);
                if (!Directory.Exists(path))
                {
                    continue;
                }
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        string name = Path.GetRelativePath(path, file).Replace('\\', '/');
                        if (!name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        names.Add(name);
                    }
                }
                catch (IOException)
                {
                    // A locked or disappearing folder only narrows the list; scanning continues with the other folders.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same as above: unreadable folders are skipped rather than failing the whole scan.
                }
            }
        }
        return [.. names];
    }

    /// <summary>Dropdown choices for a model input: every scanned model, with the workflow's own saved value kept at the front when the folders
    /// cannot see it (for example a LAN-only file), so a published workflow never loses its current model.</summary>
    /// <param name="input">The loader input name, such as "unet_name".</param>
    /// <param name="current">The workflow's current value for the input; kept as the only choice when nothing can be scanned.</param>
    /// <param name="baseDirectory">The directory relative model roots resolve against; null scans nothing.</param>
    /// <param name="roots">The model roots; null or empty scans nothing.</param>
    public static JArray Choices(string input, JToken current, string baseDirectory, IEnumerable<string> roots)
    {
        JArray choices = [.. List(input, baseDirectory, roots)];
        string name = current?.ToString() ?? "";
        if (!string.IsNullOrWhiteSpace(name) && !choices.Any(item => item.ToString() == name))
        {
            choices.Insert(0, name);
        }
        return choices;
    }

    /// <summary>Joins up to three path pieces, treating an absolute later piece as a new base the way SwarmUI's model paths do.</summary>
    private static string Combine(string a, string b, string c)
    {
        if (string.IsNullOrWhiteSpace(a))
        {
            return string.IsNullOrWhiteSpace(b) ? (c ?? "") : Path.Combine(b, c ?? "");
        }
        return Path.Combine(a, b, c);
    }
}
