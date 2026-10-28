using System.IO;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio.Storage;

/// <summary>Work storage settings (R11). Only administrators with server-settings permission can read or change locations; ordinary accounts can only ask
/// which of their own works are still waiting to be copied, as gallery paths. No API returns a location to an ordinary account (R11.4).</summary>
[API.APIClass("EcomCraft work storage: work library and export locations, background sync status and migration of old works.")]
public static class StorageApi
{
    /// <summary>Whether the session may manage storage locations.</summary>
    private static bool CanManage(Session session) => StudioApi.CanView(session) && session.User.HasPermission(Permissions.EditServerSettings);

    /// <summary>A check result for the client.</summary>
    private static JObject ProbeJson(StorageProbe.Result result) => new()
    {
        ["ok"] = result.Ok,
        ["error"] = result.Error,
        ["free_gb"] = result.FreeBytes is long free ? Math.Round(free / 1024.0 / 1024.0 / 1024.0, 1) : null,
        ["write_mbps"] = result.WriteMBps
    };

    /// <summary>The latest disk-space snapshot for administrators: the worst level and the free GB of the works folder and the work library (F46).</summary>
    internal static JObject DiskJson() => new()
    {
        ["level"] = DiskMonitor.Level(),
        ["output_free_gb"] = DiskMonitor.Status()?.OutputFree is long free ? Math.Round(free / 1024.0 / 1024.0 / 1024.0, 1) : null,
        ["library_free_gb"] = DiskMonitor.Status()?.LibraryFree is long remote ? Math.Round(remote / 1024.0 / 1024.0 / 1024.0, 1) : null
    };

    [API.APIDescription("Read the work library and export locations, the background sync queue and migration progress. Requires the server-settings permission.", "library_path: \"\", pending: 0")]
    public static Task<JObject> EcomCraftStorageSettings(Session session)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理存储位置的权限。", "bad_permissions"));
        }
        StorageSettings.Values settings = StorageSettings.Get();
        (int pending, int failing, string lastError) = WorkSync.Stats();
        WorkSync.Migration migration = WorkSync.MigrationStatus();
        return Task.FromResult(new JObject()
        {
            ["library_path"] = settings.LibraryPath,
            ["export_path"] = settings.ExportPath,
            ["previous_library_path"] = settings.PreviousLibraryPath,
            ["candidate_retention_days"] = settings.CandidateRetentionDays,
            ["disk_warn_gb"] = settings.DiskWarnGB,
            ["disk_critical_gb"] = settings.DiskCriticalGB,
            ["disk"] = DiskJson(),
            ["changed_by"] = settings.ChangedBy,
            ["changed_at"] = settings.ChangedAt > 0 ? settings.ChangedAt : null,
            ["pending"] = pending,
            ["failing"] = failing,
            ["last_error"] = lastError,
            ["migration"] = JObject.FromObject(migration)
        });
    }

    [API.APIDescription("Check and save the work library and export locations; empty keeps works only on the platform server. Nothing is saved when a check fails. Requires the server-settings permission. rawInput is the root request JSON.", "success: true, library: { ok: true }")]
    public static async Task<JObject> EcomCraftStorageSave(Session session, JObject rawInput)
    {
        if (!CanManage(session))
        {
            return StudioApi.Error("此账号没有管理存储位置的权限。", "bad_permissions");
        }
        string dataDir = Path.GetFullPath(Program.DataDir);
        string outputDir = Path.GetFullPath(Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, Program.ServerSettings.Paths.OutputPath));
        JObject checks = new();
        string[] values = new string[2];
        string[] keys = ["library_path", "export_path"];
        for (int i = 0; i < keys.Length; i++)
        {
            string raw = rawInput[keys[i]]?.Type == JTokenType.String ? rawInput.Value<string>(keys[i]).Trim() : "";
            if (raw.Length == 0)
            {
                values[i] = "";
                continue;
            }
            string error = StorageRules.Normalize(raw, dataDir, outputDir, out string normalized);
            if (error is not null)
            {
                return StudioApi.Error((i == 0 ? "作品库位置：" : "导出位置：") + error, "invalid_storage_path");
            }
            StorageProbe.Result result = await StorageProbe.CheckAsync(normalized);
            checks[i == 0 ? "library" : "export"] = ProbeJson(result);
            if (!result.Ok)
            {
                JObject failed = StudioApi.Error((i == 0 ? "作品库位置：" : "导出位置：") + result.Error, "storage_check_failed");
                failed["checks"] = checks;
                return failed;
            }
            values[i] = normalized;
        }
        StorageSettings.Values settings = StorageSettings.Get();
        if (!string.Equals(settings.LibraryPath, values[0], StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(settings.LibraryPath))
        {
            settings.PreviousLibraryPath = settings.LibraryPath;
        }
        settings.LibraryPath = values[0];
        settings.ExportPath = values[1];
        settings.CandidateRetentionDays = Math.Clamp(rawInput.Value<int?>("candidate_retention_days") ?? 14, 0, 3650);
        int warn = Math.Clamp(rawInput.Value<int?>("disk_warn_gb") ?? settings.DiskWarnGB, 0, 100_000);
        int critical = Math.Clamp(rawInput.Value<int?>("disk_critical_gb") ?? settings.DiskCriticalGB, 0, 100_000);
        if (warn > 0 && critical > warn)
        {
            return StudioApi.Error("磁盘严重不足阈值不能高于警告阈值。", "invalid_parameters");
        }
        settings.DiskWarnGB = warn;
        settings.DiskCriticalGB = critical;
        settings.ChangedBy = session.User.UserID;
        settings.ChangedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!StorageSettings.Save(settings))
        {
            return StudioApi.Error("存储设置无法保存到服务器数据目录，请检查磁盘空间和文件权限。", "storage_save_failed");
        }
        return new JObject() { ["success"] = true, ["checks"] = checks };
    }

    [API.APIDescription("Start or cancel moving works from the previous work library location to the current one (copy, verify, then delete). Requires the server-settings permission.", "success: true")]
    public static Task<JObject> EcomCraftStorageMigrate(Session session, string action)
    {
        if (!CanManage(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理存储位置的权限。", "bad_permissions"));
        }
        if (action == "cancel")
        {
            WorkSync.CancelMigration();
            return Task.FromResult(new JObject() { ["success"] = true });
        }
        if (action != "start")
        {
            return Task.FromResult(StudioApi.Error("未知的迁移操作。", "invalid_storage_action"));
        }
        StorageSettings.Values settings = StorageSettings.Get();
        if (string.IsNullOrEmpty(settings.PreviousLibraryPath) || string.IsNullOrEmpty(settings.LibraryPath)
            || string.Equals(settings.PreviousLibraryPath, settings.LibraryPath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(StudioApi.Error("没有需要迁移的旧作品库位置。", "invalid_storage_action"));
        }
        string error = WorkSync.StartMigration(settings.PreviousLibraryPath, settings.LibraryPath);
        return Task.FromResult(error is null ? new JObject() { ["success"] = true } : StudioApi.Error(error, "invalid_storage_action"));
    }

    [API.APIDescription("List the current account's works that are saved locally but not yet copied to the work library, as gallery paths. Never returns storage locations.", "pending: [\"raw/2026-10-28/a.png\"]")]
    public static Task<JObject> EcomCraftStoragePending(Session session)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        return Task.FromResult(new JObject() { ["pending"] = new JArray(WorkSync.PendingFor(session.User.UserID).Take(500)) });
    }

    [API.APIDescription("Mark works kept (picked, downloaded or exported) so the candidate cleanup never deletes them. images: [gallery path or result URL] of the current account. rawInput is the root request JSON.", "kept: 2")]
    public static Task<JObject> EcomCraftStudioMarkKept(Session session, JObject rawInput)
    {
        if (!StudioApi.CanView(session))
        {
            return Task.FromResult(StudioApi.Error("无权访问创作工作台。", "bad_permissions"));
        }
        JArray images = rawInput?["images"] as JArray;
        if (images is null || images.Count == 0 || images.Count > 500)
        {
            return Task.FromResult(StudioApi.Error("请提交 1–500 个作品路径。", "invalid_parameters"));
        }
        return Task.FromResult(new JObject() { ["kept"] = CandidateCleanup.MarkKept(session.User, images) });
    }
}
