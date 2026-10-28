using System.IO;
using System.IO.Compression;
using System.Text;
using EcomCraftStudio.Studio.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>System-data backup and restore (F45): administrators pack Data/EcomCraft (configuration, workflow snapshots, product library index)
/// together with the account database into one zip and can restore it later. Restore validates every file against the manifest's SHA-256 and requires
/// an explicit confirmation; files that are locked while the server runs (the account database) are parked in a staging folder for a manual swap.
/// Backups only read the originals and works files are never included.</summary>
[API.APIClass("EcomCraft system data backup and restore: Data/EcomCraft plus the account database, validated by manifest before restore.")]
public static class BackupService
{
    /// <summary>Largest accepted backup upload (base64 text), about 150 MB of zip content.</summary>
    private const int MaxUploadChars = 200 * 1024 * 1024;

    /// <summary>Account database files, relative to Data.</summary>
    private static readonly string[] AccountFiles = ["Users.ldb", "Users-log.ldb", "Roles.fds"];

    /// <summary>Transient folders inside Data/EcomCraft that never belong into a backup.</summary>
    private static readonly string[] ExcludedFolders = ["exports", "diagnostics", "backups", "restore-staging"];

    /// <summary>Folder of prepared backups.</summary>
    private static string Dir => Path.Combine(Program.DataDir, "EcomCraft", "backups");

    /// <summary>Folder where locked files of a restore are parked for a manual swap after shutdown.</summary>
    private static string StagingDir => Path.Combine(Program.DataDir, "EcomCraft", "restore-staging");

    [API.APIDescription("Pack Data/EcomCraft and the account database into a backup zip with a SHA-256 manifest and return its download URL. Requires the server-settings permission.", "download: \"/EcomCraft/backup/…\", file_name: \"ecomcraft-backup.zip\"")]
    public static Task<JObject> EcomCraftBackupCreate(Session session)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return Task.FromResult(StudioApi.Error("此账号没有备份系统数据的权限。", "bad_permissions"));
        }
        try
        {
            Cleanup();
            string id = Guid.NewGuid().ToString("N");
            string folder = Path.Combine(Dir, session.User.UserID);
            Directory.CreateDirectory(folder);
            string zipPath = Path.Combine(folder, $"{id}.zip");
            JObject manifest = new() { ["version"] = 1, ["created_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), ["files"] = new JArray() };
            using (FileStream stream = File.Create(zipPath))
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
            {
                foreach (string file in EnumerateBackupFiles())
                {
                    string name = EntryName(file);
                    zip.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
                    ((JArray)manifest["files"]).Add(BackupRules.Entry(name, new FileInfo(file).Length, BackupRules.Hash(file)));
                }
                ZipArchiveEntry manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using (StreamWriter writer = new(manifestEntry.Open()))
                {
                    writer.Write(manifest.ToString(Formatting.Indented));
                }
            }
            return Task.FromResult(new JObject() { ["download"] = $"/EcomCraft/backup/{id}", ["file_name"] = "ecomcraft-backup.zip", ["files"] = manifest["files"].Count() });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft backup failed: {ex.Message}");
            return Task.FromResult(StudioApi.Error("备份没有生成，请稍后重试。", "backup_failed"));
        }
    }

    [API.APIDescription("Restore a backup uploaded as base64 (data) after explicit confirmation (confirm: true). Every file is validated against the manifest's SHA-256 first; locked files are parked for a manual swap and listed in the response. Requires the server-settings permission. rawInput is the root request JSON.", "restored: 3, staged: [\"accounts/Users.ldb\"], restart_required: true")]
    public static Task<JObject> EcomCraftBackupRestore(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session) || !session.User.HasPermission(Permissions.EditServerSettings))
        {
            return Task.FromResult(StudioApi.Error("此账号没有恢复系统数据的权限。", "bad_permissions"));
        }
        if (rawInput.Value<bool?>("confirm") is not true)
        {
            return Task.FromResult(StudioApi.Error("恢复会覆盖当前系统数据，请先阅读升级说明并确认。", "backup_confirm_required"));
        }
        string data = rawInput.Value<string>("data") ?? "";
        if (data.Length == 0 || data.Length > MaxUploadChars)
        {
            return Task.FromResult(StudioApi.Error("备份文件缺失或过大，请重新选择。", "backup_invalid"));
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return Task.FromResult(StudioApi.Error("备份文件无法读取，请重新导出。", "backup_invalid"));
        }
        try
        {
            using MemoryStream memory = new(bytes);
            using ZipArchive zip = new(memory, ZipArchiveMode.Read);
            ZipArchiveEntry manifestEntry = zip.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                return Task.FromResult(StudioApi.Error("这不是 EcomCraft 备份包（缺少清单）。", "backup_invalid"));
            }
            JObject manifest;
            using (StreamReader reader = new(manifestEntry.Open()))
            {
                manifest = JObject.Parse(reader.ReadToEnd());
            }
            List<string> problems = BackupRules.Validate(zip, manifest);
            if (problems.Count > 0)
            {
                JObject failed = StudioApi.Error("备份完整性校验没有通过，没有改动任何文件。", "backup_invalid");
                failed["problems"] = new JArray(problems.Take(10));
                return Task.FromResult(failed);
            }
            return Task.FromResult(Restore(zip, manifest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or InvalidDataException)
        {
            Logs.Warning($"EcomCraft restore failed: {ex.Message}");
            return Task.FromResult(StudioApi.Error("备份包无法读取，请重新导出。", "backup_invalid"));
        }
    }

    /// <summary>Writes every validated file back; locked account files go to the staging folder. The originals are only replaced after the
    /// whole zip passed validation, and a restore is expected to be followed by a restart.</summary>
    private static JObject Restore(ZipArchive zip, JObject manifest)
    {
        List<string> restored = [];
        List<string> staged = [];
        foreach (JToken item in (JArray)manifest["files"])
        {
            string name = item["name"]?.ToString();
            ZipArchiveEntry entry = zip.GetEntry(name);
            string destination = DestinationFor(name);
            if (destination is null)
            {
                continue;
            }
            try
            {
                if (name.StartsWith("accounts/", StringComparison.Ordinal))
                {
                    // The account database is open while the server runs; on Windows it cannot be replaced directly.
                    Directory.CreateDirectory(StagingDir);
                    entry.ExtractToFile(Path.Combine(StagingDir, Path.GetFileName(name)), true);
                    staged.Add(name);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                    restored.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logs.Warning($"EcomCraft restore skipped {name}: {ex.Message}");
            }
        }
        Logs.Info($"EcomCraft restored a backup: {restored.Count} files, {staged.Count} staged for manual swap.");
        return new JObject()
        {
            ["restored"] = restored.Count,
            ["restored_files"] = new JArray(restored.Take(20)),
            ["staged"] = new JArray(staged),
            ["restart_required"] = true
        };
    }

    /// <summary>The local destination of a backup entry, or null for unknown areas (never inside works or Data roots other than the two owned ones).</summary>
    private static string DestinationFor(string name)
    {
        if (name.StartsWith("ecomcraft/", StringComparison.Ordinal) && !name.Contains(".."))
        {
            return Path.Combine(Program.DataDir, "EcomCraft", name["ecomcraft/".Length..].Replace('/', Path.DirectorySeparatorChar));
        }
        return null;
    }

    /// <summary>The backup entry name of a local file.</summary>
    private static string EntryName(string file)
    {
        string dataFull = Path.GetFullPath(Program.DataDir);
        if (file.StartsWith(dataFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "accounts/" + file[(dataFull.Length + 1)..].Replace('\\', '/');
        }
        return "ecomcraft/" + file[(Path.GetFullPath(Path.Combine(Program.DataDir, "EcomCraft")).Length + 1)..].Replace('\\', '/');
    }

    /// <summary>Every file of Data/EcomCraft except transient folders, then the account database files.</summary>
    private static IEnumerable<string> EnumerateBackupFiles()
    {
        string root = Path.Combine(Program.DataDir, "EcomCraft");
        if (Directory.Exists(root))
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", new EnumerationOptions() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                string relative = Path.GetRelativePath(root, file);
                if (ExcludedFolders.Any(folder => relative.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                yield return file;
            }
        }
        foreach (string name in AccountFiles)
        {
            string file = Path.Combine(Program.DataDir, name);
            if (File.Exists(file))
            {
                yield return file;
            }
        }
    }

    /// <summary>The account's prepared backup, or null.</summary>
    public static string ZipPath(string userId, string id)
    {
        if (string.IsNullOrEmpty(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-f]{32}$"))
        {
            return null;
        }
        string path = Path.Combine(Dir, userId, $"{id}.zip");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Deletes backups older than a day.</summary>
    private static void Cleanup()
    {
        try
        {
            if (!Directory.Exists(Dir))
            {
                return;
            }
            DateTime cutoff = DateTime.UtcNow.AddDays(-1);
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
            Logs.Debug($"EcomCraft could not clean old backups: {ex.Message}");
        }
    }
}
