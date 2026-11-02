using System.Collections.Concurrent;
using System.IO;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Downloads models from administrator-supplied links (the workspace counterpart of SwarmUI's model downloader, F62):
/// the download runs on the server in the background, progress is polled, and a finished download refreshes the model list so
/// the model becomes selectable in professional mode. State lives in memory only; nothing is written to Data/.</summary>
[API.APIClass("EcomCraft administrator model downloads with polled progress.")]
public static class ModelDownload
{
    /// <summary>How many downloads may run at the same time.</summary>
    private const int MaxConcurrent = 3;

    /// <summary>How many finished records are kept for the progress view.</summary>
    private const int MaxRecords = 20;

    /// <summary>Upper bound for the submitted URL length.</summary>
    private const int MaxUrlLength = 2000;

    /// <summary>Upper bound for the submitted file name length.</summary>
    private const int MaxNameLength = 120;

    /// <summary>One download and everything the progress view may show about it. The URL itself is never returned.</summary>
    private sealed class Download
    {
        /// <summary>Short identifier used to address the download (for example to cancel it).</summary>
        public string Id;

        /// <summary>Cleaned file name without extension.</summary>
        public string Name;

        /// <summary>Model sub-type, for example "Stable-Diffusion" or "LoRA".</summary>
        public string Type;

        /// <summary>State: "downloading", "done", "failed" or "cancelled".</summary>
        public string State = "downloading";

        /// <summary>Downloaded bytes so far.</summary>
        public long DoneBytes;

        /// <summary>Total bytes, or 0 while the server has not reported a size.</summary>
        public long TotalBytes;

        /// <summary>Bytes per second of the latest progress report.</summary>
        public long PerSecond;

        /// <summary>Plain-language reason of a failure; null otherwise.</summary>
        public string Error;

        /// <summary>When the download started, UTC.</summary>
        public DateTime StartedUtc;

        /// <summary>When the download finished, UTC; null while it runs.</summary>
        public DateTime? FinishedUtc;

        /// <summary>Cancels the running download.</summary>
        public CancellationTokenSource Canceller = new();
    }

    /// <summary>All downloads of this server run, newest last.</summary>
    private static readonly ConcurrentDictionary<string, Download> Downloads = new();

    /// <summary>Simplifies cancelling every running download at shutdown.</summary>
    private static readonly CancellationTokenSource ShutdownCanceller = new();

    /// <summary>Serializes starts and record trimming.</summary>
    private static readonly object StartLock = new();

    /// <summary>The model sub-types downloads may target, in display order.</summary>
    private static readonly string[] AllowedTypes = ["Stable-Diffusion", "LoRA", "VAE", "Embedding", "ControlNet", "Clip", "ClipVision"];

    /// <summary>Validates one start request; the returned text is a user-facing error, or null when the request is acceptable.</summary>
    private static string Validate(string url, string type, string name, out T2IModelHandler handler, out string cleanName)
    {
        handler = null;
        cleanName = null;
        url = url?.Trim() ?? "";
        if (url.Length == 0 || url.Length > MaxUrlLength || !url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            return "请填写以 http:// 或 https:// 开头的模型下载链接。";
        }
        if (!Program.T2IModelSets.TryGetValue(type ?? "", out handler) || !AllowedTypes.Contains(type))
        {
            return "请从列表中选择模型的类型。";
        }
        cleanName = Utilities.StrictFilenameClean((name ?? "").Trim().Replace(' ', '_'));
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > MaxNameLength)
        {
            return $"请填写模型保存名称（不超过 {MaxNameLength} 个字符，仅限文件名可用的文字），“.safetensors”等后缀由系统添加。";
        }
        return null;
    }

    /// <summary>The file a download writes in the end, including the gguf special case of SwarmUI's own downloader.</summary>
    private static string TargetPath(T2IModelHandler handler, string type, string name, string url, out string tempPath)
    {
        string extension = url.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? "gguf" : "safetensors";
        string folder = handler.DownloadFolderPath;
        if (extension == "gguf" && type == "Stable-Diffusion")
        {
            folder += "/../diffusion_models"; // Same special case as the native downloader: gguf checkpoints belong in diffusion_models.
        }
        tempPath = $"{folder}/{name}.download.tmp";
        return $"{folder}/{name}.{extension}";
    }

    /// <summary>Starts one download in the background; returns an error object instead when it cannot start.</summary>
    private static JObject TryStart(Session session, string url, string type, string name, out bool started)
    {
        started = false;
        string error = Validate(url, type, name, out T2IModelHandler handler, out string cleanName);
        if (error is not null)
        {
            return StudioApi.Error(error, "model_download_input");
        }
        string targetPath = TargetPath(handler, type, cleanName, url.Trim(), out string tempPath);
        if (File.Exists(targetPath))
        {
            return StudioApi.Error("服务器上已有同名模型文件；请换一个保存名称，或先删除旧文件。", "model_download_exists");
        }
        if (ModelsAPI.TryGetRefusalForModel(session, cleanName, out JObject refusal))
        {
            return refusal;
        }
        lock (StartLock)
        {
            if (Downloads.Values.Count(d => d.State == "downloading") >= MaxConcurrent)
            {
                return StudioApi.Error($"同时进行的下载最多 {MaxConcurrent} 个；请等其中一个结束再开始新的下载。", "model_download_limit");
            }
            if (Downloads.Values.Any(d => d.State == "downloading" && d.Name == cleanName && d.Type == type))
            {
                return StudioApi.Error("这个模型正在下载中。", "model_download_exists");
            }
            Download download = new()
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = cleanName,
                Type = type,
                StartedUtc = DateTime.UtcNow
            };
            Downloads[download.Id] = download;
            TrimRecords();
            _ = Task.Run(() => RunAsync(session, download, url.Trim(), handler, targetPath, tempPath));
            started = true;
            return new JObject() { ["id"] = download.Id, ["state"] = "downloading" };
        }
    }

    /// <summary>Drops the oldest finished records above the cap; never touches running downloads.</summary>
    private static void TrimRecords()
    {
        List<Download> finished = [.. Downloads.Values.Where(d => d.State != "downloading").OrderBy(d => d.FinishedUtc ?? d.StartedUtc)];
        while (finished.Count > MaxRecords && Downloads.TryRemove(finished[0].Id, out _))
        {
            finished.RemoveAt(0);
        }
    }

    /// <summary>Runs one download to the end: progress updates the record, success refreshes the model handler, failure leaves a plain reason.</summary>
    private static async Task RunAsync(Session session, Download download, string url, T2IModelHandler handler, string targetPath, string tempPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            Logs.Info($"EcomCraft model download '{download.Name}' ({download.Type}) started by {session.User.UserID}");
            using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(ShutdownCanceller.Token, download.Canceller.Token);
            await Utilities.DownloadFile(url, tempPath, (done, total, perSecond) =>
            {
                download.DoneBytes = done;
                download.TotalBytes = total;
                download.PerSecond = perSecond;
            }, cancel, session: session);
            File.Move(tempPath, targetPath);
            using (ManyReadOneWriteLock.WriteClaim claim = Program.RefreshLock.LockWrite())
            {
                handler.Refresh();
            }
            download.State = "done";
            download.FinishedUtc = DateTime.UtcNow;
            Logs.Info($"EcomCraft model download '{download.Name}' finished");
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            download.State = "cancelled";
            download.Error = "下载已取消。";
            download.FinishedUtc = DateTime.UtcNow;
            TryDeleteTemp(tempPath);
        }
        catch (Exception ex)
        {
            download.State = "failed";
            download.Error = ex is SwarmReadableErrorException ? $"下载失败：{ex.Message}" : "下载失败：无法连接下载地址或文件不可用，请检查链接后重试。";
            download.FinishedUtc = DateTime.UtcNow;
            Logs.Warning($"EcomCraft model download '{download.Name}' failed: {ex.GetType().Name}");
            TryDeleteTemp(tempPath);
        }
        finally
        {
            download.Canceller.Dispose();
        }
    }

    /// <summary>Removes a partial download file; a failure to delete is only logged.</summary>
    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logs.Warning($"EcomCraft could not remove the partial model file: {ex.GetType().Name}");
        }
    }

    /// <summary>Whether the session may use the downloader.</summary>
    private static bool CanUse(Session session) => StudioApi.CanView(session) && session.User.HasPermission(Permissions.EditServerSettings);

    [API.APIDescription("Start a model download from a link (administrators): the file is downloaded on the server in the background, "
        + "into the folder of the chosen model type, and the model list is refreshed automatically when it finishes. "
        + "Progress comes from EcomCraftStudioModelDownloadStatus; at most 3 downloads run at a time.",
        "id: \"a1b2c3d4e5f6\", state: \"downloading\"")]
    public static Task<JObject> EcomCraftStudioModelDownloadStart(Session session, string url = "", string type = "Stable-Diffusion", string name = "")
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理服务器的权限。", "bad_permissions"));
        }
        JObject result = TryStart(session, url, type, name, out bool started);
        return Task.FromResult(result);
    }

    [API.APIDescription("The progress of administrator model downloads (administrators): current and recent downloads of this server run with "
        + "state, bytes and speed. A finished download keeps its record until the list grows past 20 entries. The submitted URLs are not returned.",
        "downloads: [{ id: \"a1b2c3d4e5f6\", name: \"model\", type: \"Stable-Diffusion\", state: \"downloading\", done_bytes: 0, total_bytes: 0, per_second: 0 }], types: [\"Stable-Diffusion\"]")]
    public static Task<JObject> EcomCraftStudioModelDownloadStatus(Session session)
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理服务器的权限。", "bad_permissions"));
        }
        JArray list = new();
        foreach (Download download in Downloads.Values.OrderByDescending(d => d.StartedUtc))
        {
            list.Add(new JObject()
            {
                ["id"] = download.Id,
                ["name"] = download.Name,
                ["type"] = download.Type,
                ["state"] = download.State,
                ["done_bytes"] = download.DoneBytes,
                ["total_bytes"] = download.TotalBytes,
                ["per_second"] = download.PerSecond,
                ["error"] = download.Error,
                ["started_at"] = (long)(download.StartedUtc - DateTime.UnixEpoch).TotalSeconds,
                ["finished_at"] = download.FinishedUtc is null ? null : (long)((DateTime)download.FinishedUtc - DateTime.UnixEpoch).TotalSeconds
            });
        }
        return Task.FromResult(new JObject()
        {
            ["downloads"] = list,
            ["types"] = new JArray(AllowedTypes.Where(type => Program.T2IModelSets.ContainsKey(type)))
        });
    }

    [API.APIDescription("Cancel one running model download (administrators). A cancelled download stops soon and keeps a \"cancelled\" record.",
        "success: true")]
    public static Task<JObject> EcomCraftStudioModelDownloadCancel(Session session, string id = "")
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有管理服务器的权限。", "bad_permissions"));
        }
        if (!Downloads.TryGetValue(id ?? "", out Download download) || download.State != "downloading")
        {
            return Task.FromResult(StudioApi.Error("没有找到这个下载任务；它可能已经结束。请刷新列表。", "model_download_not_found"));
        }
        try
        {
            download.Canceller.Cancel();
        }
        catch (Exception ex) when (ex is ObjectDisposedException)
        {
            // The download already ended between the check and the cancel.
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }

    /// <summary>Cancels running downloads when the server shuts down.</summary>
    public static void Shutdown()
    {
        ShutdownCanceller.Cancel();
    }
}
