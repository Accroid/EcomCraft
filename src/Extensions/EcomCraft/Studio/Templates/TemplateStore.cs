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
        return TemplateValidator.CheckPlan(plan, SnapshotParameters(plan.Workflow));
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
        List<string> problems = TemplateValidator.CheckPlan(template.LocalPlan, SnapshotParameters(template.LocalPlan.Workflow));
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
