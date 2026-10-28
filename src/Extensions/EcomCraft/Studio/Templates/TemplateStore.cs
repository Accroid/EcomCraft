using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio.Templates;

/// <summary>Loads templates: built-in ones ship in the extension folder (Templates/builtin/*.json, one per file) and administrators' own templates live in
/// Data/EcomCraft/templates.json, kept apart so updates never overwrite custom work. Invalid templates are kept with their reasons and never offered to users.</summary>
public static class TemplateStore
{
    /// <summary>One loaded template: the parsed template (null when invalid), its ID, where it came from, why it is invalid, and the original JSON for administrators.</summary>
    public sealed record class Entry(EcomTemplate Template, string Id, string Source, List<string> Errors, JObject Raw);

    /// <summary>Folder of built-in template files and their example images.</summary>
    private static string BuiltinDir;

    /// <summary>Templates as last loaded.</summary>
    private static List<Entry> Cache = [];

    /// <summary>File names and times the cache was built from.</summary>
    private static string CacheStamp;

    /// <summary>Protects the cache and the custom file.</summary>
    private static readonly object Lock = new();

    /// <summary>Upper bound for custom templates.</summary>
    public const int MaxCustomTemplates = 200;

    /// <summary>File of administrator templates.</summary>
    private static string CustomFile => Path.Combine(Program.DataDir, "EcomCraft", "templates.json");

    /// <summary>Folder of administrator template example images.</summary>
    public static string CustomImageDir => Path.Combine(Program.DataDir, "EcomCraft", "template-images");

    /// <summary>Sets the extension folder that holds the built-in templates.</summary>
    public static void Init(string extensionFolder)
    {
        BuiltinDir = Path.Combine(extensionFolder, "Templates", "builtin");
    }

    /// <summary>The platform list with names, region and export presets: Data/EcomCraft/platforms.json when an administrator provided one, otherwise the built-in
    /// Templates/platforms.json. Unreadable files give an empty list.</summary>
    public static List<JObject> Platforms()
    {
        try
        {
            string custom = Path.Combine(Program.DataDir, "EcomCraft", "platforms.json");
            string file = File.Exists(custom) ? custom : Path.Combine(Path.GetDirectoryName(BuiltinDir) ?? "", "platforms.json");
            if (!File.Exists(file))
            {
                return [];
            }
            return [.. (JObject.Parse(File.ReadAllText(file))["platforms"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(platform => System.Text.RegularExpressions.Regex.IsMatch(platform["id"]?.ToString() ?? "", "^[a-z0-9][a-z0-9-]{0,47}$") && platform["name"] is JObject)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the platform list: {ex.Message}");
            return [];
        }
    }

    /// <summary>The upload quality thresholds of the platform file (F38): minimum side in pixels, Laplacian-variance floor for sharpness and the smallest
    /// share of opaque pixels accepted in a cut-out. Defaults apply when the file names none.</summary>
    public static (int MinSide, double BlurVariance, double MinCutoutRatio) UploadQuality()
    {
        try
        {
            string custom = Path.Combine(Program.DataDir, "EcomCraft", "platforms.json");
            string file = File.Exists(custom) ? custom : Path.Combine(Path.GetDirectoryName(BuiltinDir) ?? "", "platforms.json");
            if (!File.Exists(file))
            {
                return Default;
            }
            JObject quality = JObject.Parse(File.ReadAllText(file))["upload_quality"] as JObject;
            return (
                Math.Clamp(quality?.Value<int?>("min_side") ?? Default.MinSide, 64, 8192),
                Math.Clamp(quality?.Value<double?>("blur_variance") ?? Default.BlurVariance, 0, 10_000),
                Math.Clamp(quality?.Value<double?>("min_cutout_ratio") ?? Default.MinCutoutRatio, 0, 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    /// <summary>Default upload quality thresholds, used whenever the platform file does not override them.</summary>
    public static (int MinSide, double BlurVariance, double MinCutoutRatio) Default => (512, 30.0, 0.08);

    /// <summary>Folder holding a template's example image.</summary>
    public static string ImageDir(string source) => source == "builtin" ? BuiltinDir : CustomImageDir;

    /// <summary>All templates, reloaded when a template file changed. Structure problems are included; snapshot problems are checked by <see cref="Problems"/>.</summary>
    public static List<Entry> All()
    {
        lock (Lock)
        {
            string stamp = Stamp();
            if (stamp != CacheStamp)
            {
                Cache = Load();
                CacheStamp = stamp;
                foreach (Entry entry in Cache.Where(entry => entry.Errors.Count > 0))
                {
                    Logs.Warning($"EcomCraft template '{entry.Id}' ({entry.Source}) is not offered: {string.Join(" ", entry.Errors)}");
                }
            }
            return [.. Cache];
        }
    }

    /// <summary>The template with this ID, or null.</summary>
    public static Entry Find(string id) => string.IsNullOrWhiteSpace(id) ? null : All().FirstOrDefault(entry => entry.Id == id);

    /// <summary>Every reason a template cannot run now: structure problems, plus the local plan checked against its published snapshot.</summary>
    public static List<string> Problems(Entry entry)
    {
        if (entry.Template is null)
        {
            return entry.Errors;
        }
        ExecutionPlan plan = entry.Template.LocalPlan;
        return TemplateValidator.CheckPlan(plan, SnapshotParameters(WorkflowIdFor(plan)), entry.Template.PreserveProduct, entry.Template.BrandImageParams);
    }

    /// <summary>The published snapshot a plan runs. "*/path" picks the first published snapshot (by ID) whose path after the server name matches,
    /// so built-in templates can name a conventional workflow without knowing the server; null when none is published.</summary>
    public static string WorkflowIdFor(ExecutionPlan plan)
    {
        if (plan?.Workflow is null || !plan.Workflow.StartsWith(TemplateValidator.AnyServerPrefix, StringComparison.Ordinal))
        {
            return plan?.Workflow;
        }
        string path = plan.Workflow[TemplateValidator.AnyServerPrefix.Length..];
        return WorkflowReview.All().Where(pair => pair.Value.Snapshot is not null && pair.Key.Contains('/')
                && pair.Key[(pair.Key.IndexOf('/') + 1)..].Equals(path, StringComparison.OrdinalIgnoreCase) && WorkflowReview.IsPublished(pair.Key))
            .Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>Parameter definitions of a published snapshot, or null when the ID is not a published EcomCraft snapshot.</summary>
    public static JObject SnapshotParameters(string workflowId)
    {
        WorkflowReview.Restore();
        if (string.IsNullOrWhiteSpace(workflowId) || !WorkflowReview.IsPublished(workflowId)
            || !ComfyUIBackendExtension.CustomWorkflows.TryGetValue(workflowId, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow) || workflow is null)
        {
            return null;
        }
        return StudioApi.ParseWorkflowObject(workflow.CustomParams);
    }

    /// <summary>Page-field defaults of a published snapshot (for example "width" and "height"), or an empty object.</summary>
    public static JObject SnapshotFormDefaults(string workflowId)
    {
        JObject parameters = SnapshotParameters(workflowId);
        if (parameters is null || !ComfyUIBackendExtension.CustomWorkflows.TryGetValue(workflowId, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow) || workflow is null)
        {
            return new JObject();
        }
        return NativeWorkflowParameters.FormDefaults(parameters, StudioApi.ParseWorkflowObject(workflow.ParamValues) ?? new JObject());
    }

    /// <summary>Names and times of the template files, to notice changes cheaply.</summary>
    private static string Stamp()
    {
        List<string> parts = [];
        try
        {
            if (BuiltinDir is not null && Directory.Exists(BuiltinDir))
            {
                foreach (string file in Directory.EnumerateFiles(BuiltinDir, "*.json").Order(StringComparer.Ordinal))
                {
                    parts.Add($"{file}|{File.GetLastWriteTimeUtc(file).Ticks}");
                }
            }
            if (File.Exists(CustomFile))
            {
                parts.Add($"{CustomFile}|{File.GetLastWriteTimeUtc(CustomFile).Ticks}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            parts.Add("unreadable");
        }
        return string.Join("\n", parts);
    }

    /// <summary>Reads both sources. A custom template that reuses a built-in ID is rejected.</summary>
    private static List<Entry> Load()
    {
        List<Entry> entries = [];
        if (BuiltinDir is not null && Directory.Exists(BuiltinDir))
        {
            foreach (string file in Directory.EnumerateFiles(BuiltinDir, "*.json").Order(StringComparer.Ordinal))
            {
                JObject raw = null;
                try
                {
                    raw = JObject.Parse(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    entries.Add(new Entry(null, Path.GetFileNameWithoutExtension(file), "builtin", ["文件无法读取或不是有效的 JSON。"], null));
                    continue;
                }
                entries.Add(Make(raw, "builtin", entries));
            }
        }
        foreach (JObject raw in ReadCustom())
        {
            entries.Add(Make(raw, "custom", entries));
        }
        return entries;
    }

    /// <summary>Parses one template and checks its ID is unused.</summary>
    private static Entry Make(JObject raw, string source, List<Entry> existing)
    {
        List<string> errors = [];
        EcomTemplate template = TemplateValidator.Parse(raw, errors);
        string id = template?.Id ?? raw?["id"]?.ToString() ?? "";
        if (existing.Any(entry => entry.Id == id))
        {
            errors.Add($"ID“{id}”已被其他模板使用。");
            template = null;
        }
        if (template is not null)
        {
            template.Source = source;
        }
        return new Entry(errors.Count == 0 ? template : null, id, source, errors, raw);
    }

    /// <summary>The administrator templates as stored. A missing file means none; an unreadable file is logged and treated as none without being overwritten.</summary>
    private static List<JObject> ReadCustom()
    {
        try
        {
            if (!File.Exists(CustomFile))
            {
                return [];
            }
            return [.. (JObject.Parse(File.ReadAllText(CustomFile))["templates"] as JArray ?? new JArray()).OfType<JObject>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Logs.Error($"EcomCraft could not read the custom template file: {ex.Message}");
            return [];
        }
    }

    /// <summary>Adds or replaces an administrator template after checking its structure and snapshot. Returns an error, or null when saved.</summary>
    public static string SaveCustom(JObject raw)
    {
        List<string> errors = [];
        EcomTemplate template = TemplateValidator.Parse(raw, errors);
        if (template is null)
        {
            return string.Join(" ", errors);
        }
        List<string> problems = TemplateValidator.CheckPlan(template.LocalPlan, SnapshotParameters(WorkflowIdFor(template.LocalPlan)), template.PreserveProduct, template.BrandImageParams);
        if (problems.Count > 0)
        {
            return string.Join(" ", problems);
        }
        lock (Lock)
        {
            if (All().Any(entry => entry.Source == "builtin" && entry.Id == template.Id))
            {
                return $"ID“{template.Id}”属于内置模板，请换一个 ID。";
            }
            List<JObject> custom = ReadCustom();
            custom.RemoveAll(item => item["id"]?.ToString() == template.Id);
            if (custom.Count >= MaxCustomTemplates)
            {
                return $"自定义模板最多 {MaxCustomTemplates} 个。";
            }
            custom.Add(raw);
            return WriteCustom(custom);
        }
    }

    /// <summary>Removes an administrator template. Returns an error, or null when removed.</summary>
    public static string DeleteCustom(string id)
    {
        lock (Lock)
        {
            List<JObject> custom = ReadCustom();
            if (custom.RemoveAll(item => item["id"]?.ToString() == id) == 0)
            {
                return "没有这个自定义模板。";
            }
            return WriteCustom(custom);
        }
    }

    /// <summary>Writes the custom file through a temporary file. Callers hold <see cref="Lock"/>.</summary>
    private static string WriteCustom(List<JObject> custom)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CustomFile));
            string temp = $"{CustomFile}.tmp";
            File.WriteAllText(temp, new JObject() { ["version"] = 1, ["templates"] = new JArray(custom) }.ToString(Formatting.Indented));
            File.Move(temp, CustomFile, true);
            CacheStamp = null;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Error($"EcomCraft could not save the custom template file: {ex.Message}");
            return "模板无法保存到服务器数据目录，请检查磁盘空间和文件权限。";
        }
    }
}
