using System.IO;
using System.Net;
using System.Net.Http;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio;

/// <summary>Discovers native ComfyUI workflows on execution servers in the background. Catalog and generation requests only read the cache.</summary>
public static class WorkflowDiscovery
{
    /// <summary>Minimum interval between background refreshes.</summary>
    private const int RefreshIntervalMs = 30_000;

    /// <summary>Upper bound for one workflow file.</summary>
    private const int MaxWorkflowBytes = 2 * 1024 * 1024;

    /// <summary>Upper bound for workflow files read from one directory or server.</summary>
    private const int MaxWorkflowsPerSource = 500;

    /// <summary>Server ID for workflows found in a local directory that no registered execution server owns.</summary>
    public const int UnregisteredSource = -1;

    /// <summary>One imported workflow, the execution server it was found on, and whether it came over HTTP.</summary>
    private sealed record class Entry(ComfyUIBackendExtension.ComfyCustomWorkflow Workflow, int ServerId, bool Remote);

    /// <summary>One workflow file read during a refresh, before naming and conversion.</summary>
    private sealed record class Found(string Prefix, int ServerId, bool Remote, string RelativeName, string SourceKey, string Json);

    /// <summary>Current imports by workflow name. Replaced as a whole after each refresh.</summary>
    private static Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Protects <see cref="Entries"/> and registry updates.</summary>
    private static readonly object EntriesLock = new();

    /// <summary>Protects refresh scheduling.</summary>
    private static readonly object RefreshLock = new();

    /// <summary>The running or most recent refresh, or null before the first one.</summary>
    private static Task RefreshTask;

    /// <summary><see cref="Environment.TickCount64"/> when the most recent refresh started.</summary>
    private static long LastRefreshStart;

    /// <summary>Whether any refresh has finished since startup.</summary>
    private static volatile bool HasCompleted;

    /// <summary>Stops background refreshes when the extension shuts down.</summary>
    private static readonly CancellationTokenSource StopSource = new();

    /// <summary>Starts a background refresh when the cache is stale, waits briefly only before the first result exists, then restores cached imports to the workflow registry.</summary>
    public static void EnsureFresh()
    {
        Task task;
        lock (RefreshLock)
        {
            bool idle = RefreshTask is null || RefreshTask.IsCompleted;
            bool stale = RefreshTask is null || Environment.TickCount64 - LastRefreshStart >= RefreshIntervalMs;
            if (idle && stale && !StopSource.IsCancellationRequested)
            {
                LastRefreshStart = Environment.TickCount64;
                RefreshTask = Task.Run(RefreshAsync);
            }
            task = RefreshTask;
        }
        if (!HasCompleted && task is not null)
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // RefreshAsync logs its own failures.
            }
        }
        Restore();
    }

    /// <summary>Re-adds cached imports after a ComfyUI workflow registry reload. Never replaces a stored workflow that has the same name.</summary>
    public static void Restore()
    {
        lock (EntriesLock)
        {
            foreach (KeyValuePair<string, Entry> pair in Entries)
            {
                ComfyUIBackendExtension.CustomWorkflows.TryAdd(pair.Key, pair.Value.Workflow);
            }
        }
    }

    /// <summary>Whether the workflow was imported by discovery rather than saved in SwarmUI.</summary>
    public static bool IsNative(string name)
    {
        if (name is null)
        {
            return false;
        }
        lock (EntriesLock)
        {
            return Entries.ContainsKey(name);
        }
    }

    /// <summary>Stops future refreshes. A running refresh observes the cancellation and exits.</summary>
    public static void Shutdown()
    {
        StopSource.Cancel();
    }

    /// <summary>Reads all sources, then swaps the cache. Sources that fail keep their previous imports.</summary>
    private static async Task RefreshAsync()
    {
        try
        {
            using CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(StopSource.Token, Program.GlobalProgramCancel);
            List<BackendHandler.T2IBackendData> servers = [.. ExecutionServers.All];
            List<JObject> schemas = [.. servers.Select(ExecutionServers.Schema).Where(schema => schema is not null)];
            List<Found> found = [];
            bool localComplete = ReadLocal(servers, found, cancel.Token);
            List<BackendHandler.T2IBackendData> remotes = [.. servers.Where(data => data.Backend is ComfyUIAPIBackend)];
            Task<List<Found>>[] remoteTasks = [.. remotes.Select(data => ReadRemoteAsync(data, cancel.Token))];
            HashSet<int> refreshedServers = [];
            for (int i = 0; i < remotes.Count; i++)
            {
                try
                {
                    found.AddRange(await remoteTasks[i]);
                    refreshedServers.Add(remotes[i].ID);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or IOException)
                {
                    Logs.Debug($"EcomCraft could not list workflows on execution server {remotes[i].ID}: {ex.GetType().Name}");
                }
            }
            cancel.Token.ThrowIfCancellationRequested();
            HashSet<int> existingServers = [.. servers.Select(data => data.ID)];
            Dictionary<string, Entry> previous;
            lock (EntriesLock)
            {
                previous = Entries;
            }
            Dictionary<string, Entry> next = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Entry> pair in previous)
            {
                Entry entry = pair.Value;
                bool keepLocal = !entry.Remote && !localComplete;
                bool keepRemote = entry.Remote && existingServers.Contains(entry.ServerId) && !refreshedServers.Contains(entry.ServerId);
                if (keepLocal || keepRemote)
                {
                    next[pair.Key] = entry;
                }
            }
            foreach (Found item in found)
            {
                if (!item.Remote && !localComplete)
                {
                    continue; // A partial local scan keeps the previous local imports instead.
                }
                string name = UniqueName(item, next);
                if (name is null)
                {
                    continue;
                }
                ComfyUIBackendExtension.ComfyCustomWorkflow workflow = Convert(name, item, schemas);
                if (workflow is not null)
                {
                    next[name] = new Entry(workflow, item.ServerId, item.Remote);
                }
            }
            lock (EntriesLock)
            {
                foreach (KeyValuePair<string, Entry> pair in Entries)
                {
                    if (!next.ContainsKey(pair.Key))
                    {
                        ComfyUIBackendExtension.CustomWorkflows.TryRemove(new KeyValuePair<string, ComfyUIBackendExtension.ComfyCustomWorkflow>(pair.Key, pair.Value.Workflow));
                    }
                }
                foreach (KeyValuePair<string, Entry> pair in next)
                {
                    if (Entries.TryGetValue(pair.Key, out Entry old) && !ReferenceEquals(old.Workflow, pair.Value.Workflow))
                    {
                        ComfyUIBackendExtension.CustomWorkflows.TryUpdate(pair.Key, pair.Value.Workflow, old.Workflow);
                    }
                }
                Entries = next;
            }
            Restore();
        }
        catch (OperationCanceledException)
        {
            // Shutdown or program exit.
        }
        catch (Exception ex)
        {
            Logs.Warning($"EcomCraft workflow discovery failed: {ex.Message}");
        }
        finally
        {
            HasCompleted = true;
        }
    }

    /// <summary>Picks "Server/relative path"; avoids stored SwarmUI workflows and other sources with a short path hash.
    /// Names are passed through <see cref="Utilities.StrictFilenameClean"/> here because SwarmUI cleans the custom-workflow
    /// name again when it reads the workflow for generation; a name that changes under cleaning would resolve to nothing.</summary>
    private static string UniqueName(Found item, Dictionary<string, Entry> taken)
    {
        // The first '/' separates the server from the path, so a '/' inside the server name must not survive.
        string baseName = Utilities.StrictFilenameClean($"{item.Prefix.Replace('/', '-').Replace('\\', '-')}/{item.RelativeName}");
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }
        bool isFree(string candidate) => !taken.ContainsKey(candidate)
            && (!ComfyUIBackendExtension.CustomWorkflows.TryGetValue(candidate, out ComfyUIBackendExtension.ComfyCustomWorkflow existing) || IsOwnedImport(candidate, existing));
        if (isFree(baseName))
        {
            return baseName;
        }
        string suffix = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(item.SourceKey)))[..8];
        string hashed = Utilities.StrictFilenameClean($"{baseName} ({suffix})");
        return isFree(hashed) ? hashed : null;
    }

    /// <summary>Whether a registry entry is the one this discovery previously imported under that name.</summary>
    private static bool IsOwnedImport(string name, ComfyUIBackendExtension.ComfyCustomWorkflow existing)
    {
        lock (EntriesLock)
        {
            return Entries.TryGetValue(name, out Entry entry) && ReferenceEquals(entry.Workflow, existing);
        }
    }

    /// <summary>Converts a canvas into the server-owned prompt and parameter bindings; returns null for unsupported graphs.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow Convert(string name, Found item, List<JObject> schemas)
    {
        JObject prompt = NativeWorkflowConverter.Convert(StudioApi.ParseWorkflowObject(item.Json), out JObject metadata, out JObject values);
        if (prompt is null)
        {
            return null;
        }
        NativeWorkflowParameters.AddChoices(metadata, schemas);
        string description = item.ServerId == UnregisteredSource
            ? "从本机 ComfyUI 安装目录自动发现。执行时由平台分配到具备所需模型和节点的执行服务器。"
            : $"从执行服务器“{item.Prefix}”的 ComfyUI 自动发现。执行时由平台分配到具备所需模型和节点的执行服务器。";
        return new(name, item.Json, prompt.ToString(Formatting.None), metadata.ToString(Formatting.None), values.ToString(Formatting.None), null, description, true);
    }

    /// <summary>Scans the verified local workflow folders. Returns false when the time budget ran out before every folder was read.</summary>
    private static bool ReadLocal(List<BackendHandler.T2IBackendData> servers, List<Found> found, CancellationToken cancel)
    {
        List<BackendHandler.T2IBackendData> localServers = [.. servers.Where(data => data.Backend is ComfyUISelfStartBackend)];
        List<(string Root, string Args)> backends = [.. localServers.Select(data => (((ComfyUISelfStartBackend)data.Backend).ComfyPathBase, ((ComfyUISelfStartBackend)data.Backend).Settings.ExtraArgs))];
        List<string> paths = NativeWorkflowDiscovery.Directories(backends, Program.ServerSettings.EcomCraft.ComfyWorkflowSearchPaths ?? []);
        HashSet<string> visitedFiles = new(StringComparer.OrdinalIgnoreCase);
        long deadline = Environment.TickCount64 + 15_000;
        foreach (string directory in paths)
        {
            if (cancel.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                return false;
            }
            if (!Directory.Exists(directory))
            {
                continue;
            }
            (string prefix, int serverId) = LocalOwner(directory, localServers);
            string[] files;
            try
            {
                files = [.. Directory.EnumerateFiles(directory, "*.json", new EnumerationOptions
                {
                    RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                }).TakeWhile(_ => Environment.TickCount64 < deadline).Take(MaxWorkflowsPerSource)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Logs.Warning($"EcomCraft could not scan a local ComfyUI workflow directory: {ex.Message}");
                continue;
            }
            foreach (string file in files)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    return false;
                }
                try
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!visitedFiles.Add(fullPath) || new FileInfo(file).Length > MaxWorkflowBytes)
                    {
                        continue;
                    }
                    string relativeName = Path.GetRelativePath(directory, file).Replace('\\', '/').BeforeLast('.');
                    found.Add(new Found(prefix, serverId, false, relativeName, fullPath, File.ReadAllText(file)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Logs.Warning($"EcomCraft could not read a local ComfyUI workflow file: {ex.Message}");
                }
            }
        }
        return true;
    }

    /// <summary>Labels a local folder with the self-starting server that owns it, or the installation folder name when no registered server does.</summary>
    private static (string Prefix, int ServerId) LocalOwner(string directory, List<BackendHandler.T2IBackendData> localServers)
    {
        foreach (BackendHandler.T2IBackendData data in localServers)
        {
            try
            {
                string root = ((ComfyUISelfStartBackend)data.Backend).ComfyPathBase;
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }
                root = Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return (ExecutionServers.DisplayName(data), data.ID);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Misconfigured start script; fall back to the folder label.
            }
        }
        return (NativeWorkflowDiscovery.SourceLabel(directory), UnregisteredSource);
    }

    /// <summary>Lists and downloads workflows saved in a LAN server's ComfyUI user folder through its HTTP API.</summary>
    private static async Task<List<Found>> ReadRemoteAsync(BackendHandler.T2IBackendData data, CancellationToken cancel)
    {
        List<Found> result = [];
        if (data.Backend.Status is not (BackendStatus.RUNNING or BackendStatus.IDLE) || !data.Backend.IsEnabled)
        {
            throw new InvalidDataException("Server not running.");
        }
        ComfyUIAPIBackend backend = (ComfyUIAPIBackend)data.Backend;
        string prefix = ExecutionServers.DisplayName(data);
        string root = backend.APIAddress;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        JToken listing;
        using (CancellationTokenSource listTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
        {
            listTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync(
                $"{root}/userdata?dir=workflows&recurse=true&split=false&full_info=true", listTimeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return result;
            }
            response.EnsureSuccessStatusCode();
            listing = JToken.Parse(await response.Content.ReadAsStringAsync(listTimeout.Token));
        }
        if (listing is not JArray files)
        {
            throw new InvalidDataException("Unexpected workflow listing.");
        }
        foreach (JToken file in files.Take(MaxWorkflowsPerSource))
        {
            string path = (file is JObject info ? info["path"]?.ToString() : file.Type == JTokenType.String ? file.ToString() : null)?.Replace('\\', '/');
            long size = file is JObject sized ? sized.Value<long?>("size") ?? 0 : 0;
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith('/') || path.Split('/').Contains("..") || size > MaxWorkflowBytes)
            {
                continue;
            }
            using CancellationTokenSource fileTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            fileTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            using HttpResponseMessage fileResponse = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync(
                $"{root}/userdata/{Uri.EscapeDataString($"workflows/{path}")}", HttpCompletionOption.ResponseHeadersRead, fileTimeout.Token);
            if (!fileResponse.IsSuccessStatusCode || (fileResponse.Content.Headers.ContentLength ?? 0) > MaxWorkflowBytes)
            {
                continue;
            }
            byte[] content = await fileResponse.Content.ReadAsByteArrayAsync(fileTimeout.Token);
            if (content.Length > MaxWorkflowBytes)
            {
                continue;
            }
            result.Add(new Found(prefix, data.ID, true, path.BeforeLast('.'), $"server:{data.ID}:{path}", Encoding.UTF8.GetString(content)));
        }
        return result;
    }
}
