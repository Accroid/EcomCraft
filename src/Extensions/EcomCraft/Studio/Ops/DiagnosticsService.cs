using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>One-click diagnostics package for administrators (F44): a zip with recent logs, execution-server status, version information and the
/// EcomCraft configuration. API keys, passwords, tokens, prompts, images and session information are removed before anything is written into the zip;
/// works files and the workflow-review snapshots (node graphs) are never included. The zip is kept on the platform server for one hour and only the
/// account that created it can download it.</summary>
[API.APIClass("EcomCraft diagnostics: an administrator-only support bundle of logs, server status and sanitized configuration.")]
public static class DiagnosticsService
{
    /// <summary>How long a prepared package stays downloadable.</summary>
    private const long KeepMs = 60 * 60 * 1000;

    /// <summary>At most this much of the newest log content is included.</summary>
    private const long MaxLogBytes = 2 * 1024 * 1024;

    /// <summary>Folder of prepared packages.</summary>
    private static string Dir => Path.Combine(Program.DataDir, "EcomCraft", "diagnostics");

    /// <summary>Configuration files worth including, relative to Data/EcomCraft.</summary>
    private static readonly string[] ConfigFiles = ["storage.json", "prompting.json", "platforms.json", "templates.json"];

    [API.APIDescription("Build a diagnostics zip (recent sanitized logs, execution-server status, versions and sanitized EcomCraft configuration) and return its download URL. Requires the server-settings permission.", "download: \"/EcomCraft/diagnostics/…\", file_name: \"ecomcraft-diagnostics.zip\"")]
    public static Task<JObject> EcomCraftDiagnostics(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return Task.FromResult(StudioApi.Error("此账号没有下载诊断包的权限。", "bad_permissions"));
        }
        Cleanup();
        string id = Guid.NewGuid().ToString("N");
        string folder = Path.Combine(Dir, session.User.UserID);
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{id}.zip");
            using (FileStream stream = File.Create(path))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                WriteReport(zip, session.User.UserID);
                WriteConfigs(zip);
                WriteLog(zip);
            }
            return Task.FromResult(new JObject() { ["download"] = $"/EcomCraft/diagnostics/{id}", ["file_name"] = "ecomcraft-diagnostics.zip" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not build the diagnostics package: {ex.Message}");
            return Task.FromResult(StudioApi.Error("诊断包没有生成，请稍后重试。", "diagnostics_failed"));
        }
    }

    /// <summary>Writes report.json: versions, execution-server states (names and counts only, no addresses), storage summary and queue counts.</summary>
    private static void WriteReport(ZipArchive zip, string userId)
    {
        JObject report = new()
        {
            ["generated_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["requested_by"] = userId,
            ["versions"] = new JObject()
            {
                ["ecomcraft"] = EcomCraft.ExtensionVersion,
                ["dotnet"] = Environment.Version.ToString(),
                ["os"] = Environment.OSVersion.ToString()
            },
            ["execution_servers"] = new JArray(ExecutionServers.All.Select(data => new JObject()
            {
                ["id"] = data.ID,
                ["name"] = data.Backend?.Title ?? "",
                ["enabled"] = data.Backend?.IsEnabled ?? false,
                ["status"] = data.Backend?.Status.ToString() ?? ""
            })),
            ["storage"] = new JObject()
            {
                ["library_set"] = !string.IsNullOrEmpty(StorageSettings.Get().LibraryPath),
                ["export_set"] = !string.IsNullOrEmpty(StorageSettings.Get().ExportPath),
                ["candidate_retention_days"] = StorageSettings.Get().CandidateRetentionDays,
                ["sync_pending"] = WorkSync.Stats().Pending
            },
            ["templates_published"] = TemplateStore.All().Count(entry => TemplateStore.Problems(entry).Count == 0)
        };
        WriteEntry(zip, "report.json", report.ToString(Newtonsoft.Json.Formatting.Indented));
    }

    /// <summary>Writes the EcomCraft configuration files with every secret value removed.</summary>
    private static void WriteConfigs(ZipArchive zip)
    {
        foreach (string name in ConfigFiles)
        {
            string file = Path.Combine(Program.DataDir, "EcomCraft", name);
            if (!File.Exists(file))
            {
                continue;
            }
            string content = File.ReadAllText(file);
            string cleaned;
            try
            {
                cleaned = DiagnosticsSanitizer.RedactJson(JObject.Parse(content)).ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or IOException)
            {
                cleaned = DiagnosticsSanitizer.RedactText(content);
            }
            WriteEntry(zip, $"config/{name}", cleaned);
        }
    }

    /// <summary>Writes the newest part of the current log file with secrets removed; skipped when there is no log file.</summary>
    private static void WriteLog(ZipArchive zip)
    {
        string file = Logs.LogFilePath;
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return;
        }
        using FileStream stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long start = Math.Max(0, stream.Length - MaxLogBytes);
        stream.Seek(start, SeekOrigin.Begin);
        using StreamReader reader = new(stream);
        WriteEntry(zip, "logs/swarmui.log", DiagnosticsSanitizer.RedactText(reader.ReadToEnd()));
    }

    /// <summary>Writes one text entry.</summary>
    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream target = entry.Open();
        using StreamWriter writer = new(target);
        writer.Write(content);
    }

    /// <summary>The account's prepared package, or null.</summary>
    public static string ZipPath(string userId, string id)
    {
        if (string.IsNullOrEmpty(id) || !Regex.IsMatch(id, "^[0-9a-f]{32}$"))
        {
            return null;
        }
        string path = Path.Combine(Dir, userId, $"{id}.zip");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Deletes packages older than an hour.</summary>
    private static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Dir))
            {
                return;
            }
            DateTime cutoff = DateTime.UtcNow.AddMilliseconds(-KeepMs);
            foreach (string file in Directory.EnumerateFiles(Dir, "*.zip", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Debug($"EcomCraft could not clean old diagnostics packages: {ex.Message}");
        }
    }
}
