using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Small, local-only adapter. This is not a durable task service or a platform-wide egress policy.</summary>
[API.APIClass("EcomCraft image and text-to-video workspace. Cloud providers are not configured.")]
public static class StudioApi
{
    public const string LocalScopeKey = "ecomcraft_execution_scope";
    private const int MaxReferenceBytes = 5 * 1024 * 1024;
    private const int MaxLoras = 5;
    public const string PublishedWorkflowScopeKey = "ecomcraft_published_workflow";

    /// <summary>IDs imported from native ComfyUI directories during the preceding catalog refresh.</summary>
    private static readonly HashSet<string> NativeWorkflowIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Serializes native workflow catalog refreshes and ownership updates.</summary>
    private static readonly object NativeWorkflowLock = new();

    /// <summary>Recent progress from ComfyUI sockets owned by SwarmUI generation requests.</summary>
    private static readonly Dictionary<string, ComfyQueueProgress> ComfyProgress = new();

    /// <summary>Protects progress snapshots shared by generation and API request threads.</summary>
    private static readonly object ComfyProgressLock = new();

    private sealed class ComfyQueueProgress
    {
        public int TotalNodes;
        public int FinishedNodes;
        public HashSet<string> CompletedNodeIds = new();
        public string CurrentNode;
        public double? NodePercent;
        public long Updated = Environment.TickCount64;
    }

    /// <summary>Records server-observed progress without retaining prompt text or workflow inputs.</summary>
    public static void ObserveComfyProgress(int backendId, string promptId, int expectedNodes, JObject message)
    {
        if (string.IsNullOrWhiteSpace(promptId) || message?["data"] is not JObject data)
        {
            return;
        }
        string type = message["type"]?.ToString();
        string key = $"{backendId}:{promptId}";
        lock (ComfyProgressLock)
        {
            if (!ComfyProgress.TryGetValue(key, out ComfyQueueProgress progress))
            {
                progress = new ComfyQueueProgress();
                ComfyProgress[key] = progress;
            }
            progress.TotalNodes = Math.Max(0, expectedNodes);
            progress.Updated = Environment.TickCount64;
            if (type == "progress_state" && data["nodes"] is JObject nodes)
            {
                progress.FinishedNodes = nodes.Properties().Count(node => node.Value?["state"]?.ToString() == "finished");
                foreach (JProperty finished in nodes.Properties().Where(node => node.Value?["state"]?.ToString() == "finished"))
                {
                    progress.CompletedNodeIds.Add(finished.Name);
                }
                JProperty running = nodes.Properties().FirstOrDefault(node => node.Value?["state"]?.ToString() == "running");
                if (running is not null)
                {
                    progress.CurrentNode = running.Name;
                    double value = running.Value.Value<double?>("value") ?? 0;
                    double max = running.Value.Value<double?>("max") ?? 0;
                    progress.NodePercent = max > 0 ? Math.Clamp(value / max * 100, 0, 100) : null;
                }
                else
                {
                    progress.NodePercent = null;
                }
            }
            else if (type == "executing")
            {
                string nextNode = data["node"]?.ToString();
                if (!string.IsNullOrWhiteSpace(progress.CurrentNode) && progress.CurrentNode != nextNode)
                {
                    progress.CompletedNodeIds.Add(progress.CurrentNode);
                    progress.FinishedNodes = Math.Max(progress.FinishedNodes, progress.CompletedNodeIds.Count);
                }
                progress.CurrentNode = nextNode;
                progress.NodePercent = null;
            }
            else if (type == "execution_cached" && data["nodes"] is JArray cached)
            {
                foreach (JToken node in cached)
                {
                    progress.CompletedNodeIds.Add(node.ToString());
                }
                progress.FinishedNodes = Math.Max(progress.FinishedNodes, progress.CompletedNodeIds.Count);
            }
            else if (type == "progress" && progress.CurrentNode is not null)
            {
                double value = data.Value<double?>("value") ?? 0;
                double max = data.Value<double?>("max") ?? 0;
                // SwarmUI uses these sentinel maxima for output transfer, not sampling progress.
                if (max > 0 && max < 12345)
                {
                    progress.NodePercent = Math.Clamp(value / max * 100, 0, 100);
                }
            }
        }
    }

    private static JArray NativeQueueEntries(JArray entries, int backendId, bool running)
    {
        JArray result = new();
        if (entries is null)
        {
            return result;
        }
        foreach (JArray entry in entries.OfType<JArray>())
        {
            if (entry.Count < 3 || entry[1]?.Type != JTokenType.String || entry[2] is not JObject graph)
            {
                continue;
            }
            string promptId = entry[1].ToString();
            string key = $"{backendId}:{promptId}";
            JObject item = new()
            {
                ["backend_id"] = backendId,
                ["state"] = running ? "running" : "queued",
                ["position"] = running ? null : result.Count + 1,
                ["total_nodes"] = graph.Count,
                ["current_node"] = null,
                ["node_type"] = null,
                ["node_percent"] = null,
                ["overall_percent"] = null,
                ["finished_nodes"] = null
            };
            if (running)
            {
                lock (ComfyProgressLock)
                {
                    if (ComfyProgress.TryGetValue(key, out ComfyQueueProgress progress))
                    {
                        item["current_node"] = progress.CurrentNode;
                        item["node_percent"] = progress.NodePercent;
                        if (progress.FinishedNodes > 0 || progress.NodePercent.HasValue)
                        {
                            int total = Math.Max(1, graph.Count);
                            item["finished_nodes"] = Math.Min(progress.FinishedNodes, total);
                            item["overall_percent"] = Math.Clamp((progress.FinishedNodes + (progress.NodePercent ?? 0) / 100) / total * 100, 0, 100);
                        }
                    }
                }
                if (item["current_node"] is JValue nodeId && graph[nodeId.ToString()] is JObject node)
                {
                    string classType = node["class_type"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(classType))
                    {
                        item["node_type"] = classType.Length <= 80 ? classType : classType[..80];
                    }
                }
            }
            result.Add(item);
        }
        return result;
    }

    private static readonly HashSet<string> WorkflowFormParameterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "negativeprompt", "model", "width", "height", "images", "steps", "cfgscale", "seed", "sampler", "scheduler"
    };

    private static readonly HashSet<string> WorkflowDeniedParameterIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "comfyworkflowraw", "comfyworkflowparammetadata", "comfyuicustomworkflow", "extra_metadata", "session_id",
        "backend", "backendtype", "exactbackendid", "donotsave", "webhooks", "forwardrawbackenddata",
        "forwardswarmdata", "continueaftererrors", "nointernalSpecialHandling"
    };

    private static readonly HashSet<string> SupportedWorkflowInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "integer", "decimal", "boolean", "dropdown", "image", "model", "list"
    };

    /// <summary>Additional restriction, never an override of native backend matching or user permissions.</summary>
    public static bool ValidateLocalBackend(T2IParamInput input, BackendHandler.T2IBackendData backend)
    {
        if (!input.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start")
        {
            return true;
        }
        bool allowed = backend.Backend is ComfyUISelfStartBackend && backend.Backend.IsReal;
        if (!allowed)
        {
            input.RefusalReasons.Add("EcomCraft workspace requests only permit local self-starting ComfyUI backends.");
        }
        return allowed;
    }

    private static bool CanView(Session session) => session?.User is not null
        && session.User.HasPermission(Permissions.FundamentalGenerateTabAccess)
        && session.User.HasPermission(Permissions.FundamentalModelAccess)
        && session.User.HasPermission(Permissions.ModelParams);

    private static bool IsLocalAvailable(BackendHandler.T2IBackendData data) => data.Backend is ComfyUISelfStartBackend
        && data.Backend.IsReal && data.Backend.IsEnabled && !data.Backend.ShutDownReserve
        && data.Backend.CanLoadModels && data.Backend.MaxUsages > 0 && data.Usages < data.Backend.MaxUsages
        && data.Backend.Reservations == 0 && data.Backend.Status == BackendStatus.RUNNING;

    private static IEnumerable<T2IModel> LocalModels(Session session) =>
        Program.T2IModelSets.TryGetValue("Stable-Diffusion", out T2IModelHandler handler)
            ? handler.Models.Values.Where(m => m.IsSupportedModelType && session.User.IsAllowedModel(m.Name)) : [];

    private static List<string> LocalLoras(Session session) =>
        Program.T2IModelSets.TryGetValue("LoRA", out T2IModelHandler handler)
            ? handler.ListModelNamesFor(session).Where(n => n != "(None)").ToList() : [];

    private static JArray OptionPairs(IEnumerable<string> values)
    {
        JArray array = new();
        foreach (string value in values)
        {
            string id = value.Before("///");
            string name = value.Contains("///") ? value.After("///") : value;
            array.Add(new JObject() { ["id"] = id, ["name"] = name });
        }
        return array;
    }

    private static string Kind(T2IModel model)
    {
        T2IModelCompatClass compat = model.ModelClass?.CompatClass;
        if (compat is null || compat.IsAudioModel)
        {
            return null;
        }
        if (compat.IsText2Video)
        {
            return "video";
        }
        return compat.IsImage2Video ? null : "image";
    }

    [API.APIDescription("List local ComfyUI queue entries and active SwarmUI generation claims without disclosing prompts or session IDs.", "backends: [], native_tasks: [], tasks: []")]
    public static async Task<JObject> EcomCraftStudioQueue(Session session)
    {
        if (!CanView(session))
        {
            return Error("无权查看任务队列。", "bad_permissions");
        }
        JArray backends = new();
        JArray nativeTasks = new();
        HashSet<string> activeProgress = new();
        foreach (BackendHandler.T2IBackendData data in Program.Backends.EnumerateT2IBackends.Where(b => b.Backend is ComfyUISelfStartBackend && b.Backend.IsReal))
        {
            ComfyUISelfStartBackend backend = (ComfyUISelfStartBackend)data.Backend;
            JObject item = new()
            {
                ["id"] = data.ID,
                ["status"] = backend.Status.ToString().ToLowerFast(),
                ["running"] = JValue.CreateNull(),
                ["pending"] = JValue.CreateNull()
            };
            if (backend.Status == BackendStatus.RUNNING)
            {
                try
                {
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
                    using HttpResponseMessage response = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync($"{backend.APIAddress}/queue", timeout.Token);
                    response.EnsureSuccessStatusCode();
                    JObject queue = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
                    item["running"] = (queue["queue_running"] as JArray)?.Count;
                    item["pending"] = (queue["queue_pending"] as JArray)?.Count;
                    foreach (JArray entry in new[] { queue["queue_running"] as JArray, queue["queue_pending"] as JArray }.Where(array => array is not null).SelectMany(array => array.OfType<JArray>()))
                    {
                        if (entry.Count > 1 && entry[1]?.Type == JTokenType.String)
                        {
                            activeProgress.Add($"{data.ID}:{entry[1]}");
                        }
                    }
                    foreach (JToken task in NativeQueueEntries(queue["queue_running"] as JArray, data.ID, true))
                    {
                        nativeTasks.Add(task);
                    }
                    foreach (JToken task in NativeQueueEntries(queue["queue_pending"] as JArray, data.ID, false))
                    {
                        nativeTasks.Add(task);
                    }
                }
                catch (Exception)
                {
                    item["status"] = "unresponsive";
                }
            }
            backends.Add(item);
        }
        lock (ComfyProgressLock)
        {
            foreach (string key in ComfyProgress.Keys.Where(key => !activeProgress.Contains(key)
                && Environment.TickCount64 - ComfyProgress[key].Updated > 30000).ToArray())
            {
                ComfyProgress.Remove(key);
            }
        }
        JArray tasks = new();
        foreach (Session ownerSession in Program.Sessions.Sessions.Values)
        {
            foreach (Session.GenClaim claim in ownerSession.Claims.Values)
            {
                int waiting = Volatile.Read(ref claim.WaitingGenerations);
                int live = Volatile.Read(ref claim.LiveGens);
                int backendWait = Volatile.Read(ref claim.WaitingBackends);
                if (waiting <= 0 && live <= 0 && backendWait <= 0)
                {
                    continue;
                }
                bool mine = ownerSession.User.UserID == session.User.UserID;
                tasks.Add(new JObject()
                {
                    ["id"] = mine ? claim.ID : null,
                    ["mine"] = mine,
                    ["state"] = claim.ShouldCancel ? "stopping" : live > 0 ? "running" : backendWait > 0 ? "waiting_backend" : "queued",
                    ["count"] = Math.Max(1, waiting),
                    ["elapsed_seconds"] = Math.Max(0, (Environment.TickCount64 - claim.StartTime) / 1000),
                    ["request_id"] = mine && claim.UserRequestId > 0 ? claim.UserRequestId : null
                });
            }
        }
        return new JObject() { ["backends"] = backends, ["native_tasks"] = nativeTasks, ["tasks"] = tasks };
    }

    [API.APIDescription("Cancel one active generation claim belonging to the authenticated user.", "success: true")]
    public static Task<JObject> EcomCraftStudioCancelTask(Session session, long task_id)
    {
        if (!CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Task.FromResult(Error("此账号没有结束任务的权限。", "bad_permissions"));
        }
        foreach (Session ownerSession in session.User.CurrentSessions.Values)
        {
            if (ownerSession.Claims.TryGetValue(task_id, out Session.GenClaim claim)
                && (Volatile.Read(ref claim.WaitingGenerations) > 0 || Volatile.Read(ref claim.LiveGens) > 0))
            {
                try
                {
                    claim.LocalClaimInterrupt.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    return Task.FromResult(Error("任务已结束，请刷新队列。", "task_not_found"));
                }
                return Task.FromResult(new JObject() { ["success"] = true });
            }
        }
        return Task.FromResult(Error("任务不存在或不属于当前账号。", "task_not_found"));
    }

    private static JObject LocalBackendStatus() => new()
    {
        ["local_backends_observed"] = Program.Backends.EnumerateT2IBackends.Count(IsLocalAvailable),
        ["local_backends_starting"] = Program.Backends.EnumerateT2IBackends.Count(data => data.Backend is ComfyUISelfStartBackend
            && data.Backend.IsReal && data.Backend.IsEnabled && data.Backend.Status is BackendStatus.WAITING or BackendStatus.LOADING),
        ["local_backends_errored"] = Program.Backends.EnumerateT2IBackends.Count(data => data.Backend is ComfyUISelfStartBackend
            && data.Backend.IsReal && data.Backend.IsEnabled && data.Backend.Status == BackendStatus.ERRORED)
    };

    [API.APIDescription("Read the current availability of local self-starting ComfyUI backends.", "local_backends_observed: 0, local_backends_starting: 0, local_backends_errored: 0")]
    public static Task<JObject> EcomCraftStudioBackendStatus(Session session)
    {
        return Task.FromResult(CanView(session) ? LocalBackendStatus() : Error("无权访问创作工作台。", "bad_permissions"));
    }

    [API.APIDescription("Read authorized local model metadata and observed local backend availability. No probes or cloud calls; capabilities still require live validation.", "models: [], local_backends_observed: 0, can_generate: false, cloud_configured: false")]
    public static Task<JObject> EcomCraftStudioCatalog(Session session)
    {
        if (!CanView(session))
        {
            return Task.FromResult(Error("无权访问创作工作台或模型列表。", "bad_permissions"));
        }
        ImportNativeComfyWorkflows();
        JObject result = new()
        {
            ["user_name"] = session.User.UserID,
            ["can_generate"] = session.User.HasPermission(Permissions.BasicImageGeneration),
            ["can_generate_video"] = session.User.HasPermission(Permissions.BasicImageGeneration) && session.User.HasPermission(Permissions.ParamVideo),
            ["can_manage_settings"] = session.User.HasPermission(Permissions.EditServerSettings),
            ["cloud_configured"] = false,
            ["models"] = new JArray(LocalModels(session).Where(m => Kind(m) is not null).OrderBy(m => m.Name).Select(m => new JObject()
            {
                ["id"] = m.Name,
                ["kind"] = Kind(m),
                ["class_name"] = m.ModelClass.Name,
                ["width"] = m.StandardWidth > 0 ? m.StandardWidth : m.ModelClass.StandardWidth,
                ["height"] = m.StandardHeight > 0 ? m.StandardHeight : m.ModelClass.StandardHeight,
                ["resolution_step"] = Math.Max(8, m.ModelClass.CompatClass.ResolutionPrecision),
                ["image_reference"] = Kind(m) == "image"
            })),
            ["loras"] = new JArray(LocalLoras(session).OrderBy(n => n)),
            ["samplers"] = OptionPairs(ComfyUIBackendExtension.Samplers),
            ["schedulers"] = OptionPairs(ComfyUIBackendExtension.Schedulers),
            ["workflows"] = PublishedWorkflowCatalog(session)
        };
        foreach (KeyValuePair<string, JToken> entry in LocalBackendStatus())
        {
            result[entry.Key] = entry.Value;
        }
        return Task.FromResult(result);
    }

    /// <summary>Imports native ComfyUI canvas workflows from configured, administrator-controlled directories.</summary>
    private static void ImportNativeComfyWorkflows()
    {
        lock (NativeWorkflowLock)
        {
            ImportNativeComfyWorkflowsLocked();
        }
    }

    /// <summary>Refreshes native workflows while the catalog lock is held.</summary>
    private static void ImportNativeComfyWorkflowsLocked()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> discoveredNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string configured in Program.ServerSettings.EcomCraft.ComfyWorkflowSearchPaths ?? [])
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    paths.Add(Path.GetFullPath(configured.Trim()));
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft invalid ComfyUI workflow directory: {ex.Message}");
                }
            }
        }
        if (paths.Count == 0)
        {
            foreach (ComfyUISelfStartBackend backend in Program.Backends.EnumerateT2IBackends
                .Select(data => data.Backend as ComfyUISelfStartBackend).Where(value => value is not null))
            {
                string userDirectory = Path.Combine(backend.ComfyPathBase, "user");
                Match overrideMatch = Regex.Match(backend.Settings.ExtraArgs ?? "", @"(?:^|\s)--user-directory(?:=|\s+)(?:""([^""]+)""|'([^']+)'|([^\s]+))");
                if (overrideMatch.Success)
                {
                    try
                    {
                        string overridePath = overrideMatch.Groups[1].Success ? overrideMatch.Groups[1].Value
                            : overrideMatch.Groups[2].Success ? overrideMatch.Groups[2].Value : overrideMatch.Groups[3].Value;
                        userDirectory = Path.GetFullPath(Path.IsPathRooted(overridePath)
                            ? overridePath : Path.Combine(backend.ComfyPathBase, overridePath));
                    }
                    catch (Exception ex)
                    {
                        Logs.Warning($"EcomCraft invalid ComfyUI --user-directory: {ex.Message}");
                        continue;
                    }
                }
                paths.Add(Path.Combine(userDirectory, "default", "workflows"));
            }
        }
        foreach (string directory in paths)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Logs.Warning($"EcomCraft could not scan ComfyUI workflow directory '{directory}': {ex.Message}");
                continue;
            }
            foreach (string file in files)
            {
                try
                {
                    if (new FileInfo(file).Length > 2 * 1024 * 1024)
                    {
                        continue;
                    }
                    string relativeName = Path.GetRelativePath(directory, file).Replace('\\', '/').BeforeLast('.');
                    string name = $"ComfyUI - {relativeName.Replace('/', '-')}";
                    if (discoveredNames.Contains(name)
                        || (!NativeWorkflowIds.Contains(name) && ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name)))
                    {
                        Logs.Warning($"EcomCraft skipped duplicate native workflow name '{name}' from '{file}'.");
                        continue;
                    }
                    ComfyUIBackendExtension.ComfyCustomWorkflow imported = ConvertNativeWorkflow(name, File.ReadAllText(file));
                    if (imported is not null)
                    {
                        ComfyUIBackendExtension.CustomWorkflows[name] = imported;
                        discoveredNames.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    Logs.Warning($"EcomCraft could not import native ComfyUI workflow '{file}': {ex.Message}");
                }
            }
        }
        foreach (string name in NativeWorkflowIds)
        {
            if (!discoveredNames.Contains(name))
            {
                ComfyUIBackendExtension.CustomWorkflows.TryRemove(name, out _);
            }
        }
        NativeWorkflowIds.Clear();
        NativeWorkflowIds.UnionWith(discoveredNames);
    }

    /// <summary>Converts a native ComfyUI canvas file into the API prompt format used by SwarmUI.</summary>
    private static ComfyUIBackendExtension.ComfyCustomWorkflow ConvertNativeWorkflow(string name, string json)
    {
        JObject canvas = ParseWorkflowObject(json);
        if (canvas?["nodes"] is not JArray canvasNodes || canvasNodes.Count == 0 || canvas["links"] is not JArray links)
        {
            return null;
        }
        HashSet<string> positiveTextNodes = [];
        HashSet<string> negativeTextNodes = [];
        bool hasSampler = false;
        bool hasOutput = false;
        foreach (JObject node in canvasNodes.OfType<JObject>())
        {
            string nodeType = node["type"]?.ToString();
            hasOutput |= nodeType is "SaveImage" or "PreviewImage";
            if (!string.Equals(node["type"]?.ToString(), "KSampler", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(node["type"]?.ToString(), "KSamplerAdvanced", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            hasSampler = true;
            foreach (JObject input in (node["inputs"] as JArray)?.OfType<JObject>() ?? [])
            {
                if (input["name"]?.ToString() is not ("positive" or "negative") || input["link"] is null)
                {
                    continue;
                }
                JArray source = null;
                foreach (JToken linkToken in links)
                {
                    if (linkToken is JArray link && link.Count >= 5 && link[0].ToString() == input["link"]?.ToString())
                    {
                        source = new JArray(link[1].ToString(), link[2].DeepClone());
                        break;
                    }
                }
                if (source is not null)
                {
                    (input["name"]?.ToString() == "positive" ? positiveTextNodes : negativeTextNodes).Add(source[0].ToString());
                }
            }
        }
        if (!hasSampler || !hasOutput || positiveTextNodes.Count == 0 || negativeTextNodes.Count == 0)
        {
            return null;
        }
        JObject prompt = new();
        bool mappedPositive = false;
        bool mappedNegative = false;
        foreach (JObject node in canvasNodes.OfType<JObject>())
        {
            string id = node["id"]?.ToString();
            string type = node["type"]?.ToString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }
            JObject inputs = new();
            JObject namedValues = node["widgets_values_named"] as JObject;
            int widgetIndex = 0;
            foreach (JObject input in (node["inputs"] as JArray)?.OfType<JObject>() ?? [])
            {
                string inputName = input["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(inputName))
                {
                    continue;
                }
                JToken link = input["link"];
                JToken value = null;
                if (link is not null && link.Type != JTokenType.Null)
                {
                    foreach (JToken linkToken in links)
                    {
                        if (linkToken is JArray linkData && linkData.Count >= 5 && linkData[0].ToString() == link.ToString())
                        {
                            value = new JArray(linkData[1].ToString(), linkData[2].DeepClone());
                            break;
                        }
                    }
                    if (value is null)
                    {
                        return null;
                    }
                }
                else if (input["widget"] is not null)
                {
                    value = namedValues?[inputName]?.DeepClone();
                    if (value is null && node["widgets_values"] is JArray values && widgetIndex < values.Count)
                    {
                        value = values[widgetIndex++].DeepClone();
                        if (inputName == "seed" && widgetIndex < values.Count
                            && values[widgetIndex]?.Type == JTokenType.String
                            && values[widgetIndex].ToString() is "randomize" or "fixed" or "increment" or "decrement")
                        {
                            widgetIndex++;
                        }
                    }
                }
                if (value is not null)
                {
                    inputs[inputName] = value;
                }
            }
            if (type == "CLIPTextEncode" && inputs.ContainsKey("text"))
            {
                if (positiveTextNodes.Contains(id))
                {
                    inputs["text"] = "${prompt}";
                    mappedPositive = true;
                }
                else if (negativeTextNodes.Contains(id))
                {
                    inputs["text"] = "${negative_prompt}";
                    mappedNegative = true;
                }
            }
            if (type is "KSampler" or "KSamplerAdvanced")
            {
                ReplaceIfPresent(inputs, "seed", "%%_COMFYFIXME_${seed:-1}_ENDFIXME_%%");
                ReplaceIfPresent(inputs, "steps", "%%_COMFYFIXME_${steps:20}_ENDFIXME_%%");
                ReplaceIfPresent(inputs, "cfg", "%%_COMFYFIXME_${cfg_scale:7}_ENDFIXME_%%");
                ReplaceIfPresent(inputs, "sampler_name", "${sampler:euler}");
                ReplaceIfPresent(inputs, "scheduler", "${scheduler:normal}");
            }
            if (type == "EmptyLatentImage")
            {
                ReplaceIfPresent(inputs, "width", "%%_COMFYFIXME_${width:512}_ENDFIXME_%%");
                ReplaceIfPresent(inputs, "height", "%%_COMFYFIXME_${height:512}_ENDFIXME_%%");
                if (inputs.ContainsKey("batch_size"))
                {
                    inputs["batch_size"] = 1;
                }
            }
            if (type is "CheckpointLoaderSimple" or "CheckpointLoader")
            {
                ReplaceIfPresent(inputs, "ckpt_name", "${model}");
            }
            prompt[id] = new JObject() { ["class_type"] = type, ["inputs"] = inputs };
        }
        if (prompt.Count == 0 || !mappedPositive || !mappedNegative)
        {
            return null;
        }
        return new(name, json, prompt.ToString(Newtonsoft.Json.Formatting.None), "{}", "{}", null,
            "从 ComfyUI 工作流目录自动发现。", true);
    }

    /// <summary>Replaces a native node input only when that input exists in the source workflow.</summary>
    private static void ReplaceIfPresent(JObject inputs, string id, string value)
    {
        if (inputs.ContainsKey(id))
        {
            inputs[id] = value;
        }
    }

    [API.APIDescription("Generate using a validated allowlist on local self-starting ComfyUI only. No cloud fallback, automatic retries or durable task guarantees. rawInput is the root request JSON.", "images: [\"View/user/result.png\"]")]
    public static async Task<JObject> EcomCraftStudioGenerate(Session session, JObject rawInput)
    {
        if (!CanView(session) || !session.User.HasPermission(Permissions.BasicImageGeneration))
        {
            return Error("此账号没有生成权限。请联系管理员。", "bad_permissions");
        }
        if ((string)rawInput["source"] != "local")
        {
            return Error("云端服务尚未配置；任务未提交，也不会自动切换服务商。", "cloud_unavailable");
        }
        string requestedWorkflow = (string)rawInput["workflow_id"];
        if (!string.IsNullOrWhiteSpace(requestedWorkflow))
        {
            return await GeneratePublishedWorkflow(session, rawInput, requestedWorkflow);
        }
        string mode = (string)rawInput["mode"] ?? "text2img";
        if (mode is not ("text2img" or "img2img" or "inpaint" or "outpaint" or "upscale" or "video"))
        {
            return Error("未知的创作方式。", "invalid_mode");
        }
        string kind = (string)rawInput["kind"];
        string modelId = (string)rawInput["model"];
        T2IModel model = LocalModels(session).FirstOrDefault(m => m.Name == modelId);
        if (model is null || Kind(model) != kind || kind is not ("image" or "video"))
        {
            return Error("模型不可用、权限已变化或模型类型不匹配。请刷新模型列表。", "invalid_model");
        }
        if (kind == "video" || mode == "video")
        {
            if (mode != "video" || !session.User.HasPermission(Permissions.ParamVideo))
            {
                return Error("此账号没有视频参数权限，或视频流程选择有误。请联系管理员。", "bad_permissions");
            }
        }
        if (!Program.Backends.EnumerateT2IBackends.Any(IsLocalAvailable))
        {
            return Error("没有观测到可用的部署机 ComfyUI 节点。请由管理员检查原生引擎；不会转到远程或云端。", "local_unavailable");
        }
        string prompt = (string)rawInput["prompt"];
        string negative = (string)rawInput["negativeprompt"] ?? "";
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 12000 || negative.Length > 6000)
        {
            return Error("请填写创作描述（最多 12000 字符），排除内容最多 6000 字符。", "invalid_prompt");
        }
        // Native prompt directives may change parameters or apply presets after parsing. This adapter accepts plain text only.
        if (new[] { prompt, negative }.Any(p => p.Contains('<') || p.Contains('>') || p.Contains("__")))
        {
            return Error("本版工作台只接受普通描述，不支持 <…> 原生指令、预设或 __通配符__。高级工作流请使用原生引擎。", "unsupported_prompt_syntax");
        }
        int step = Math.Max(8, model.ModelClass.CompatClass.ResolutionPrecision);
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, kind == "video" ? 1 : 4, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–4 张，视频每批 1 条。", "invalid_parameters");
        }
        // Build fresh input: never forward caller-supplied presets, workflow JSON, routes, metadata or backend selectors.
        JObject input = new()
        {
            ["model"] = model.Name, ["prompt"] = prompt, ["negativeprompt"] = negative,
            ["width"] = width, ["height"] = height, ["images"] = images,
            ["webhooks"] = "None", ["donotsave"] = false,
            ["extra_metadata"] = new JObject() { [LocalScopeKey] = "local_self_start" }
        };
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, uint.MaxValue, true)
            || !CopyOptionalNumber(rawInput, input, "steps", 1, 100, true)
            || !CopyOptionalNumber(rawInput, input, "cfgscale", 0, 30, false))
        {
            return Error("种子、步数或提示词强度不在允许范围内。", "invalid_parameters");
        }

        // ----- Reference / edit images -----
        string reference = (string)rawInput["initimage"];
        bool needsReference = mode is "img2img" or "inpaint" or "outpaint" or "upscale";
        if (needsReference && string.IsNullOrEmpty(reference))
        {
            return Error("该创作方式需要先提供初始图。", "invalid_reference");
        }
        if (!string.IsNullOrEmpty(reference))
        {
            if (kind != "image" || !ValidReference(reference))
            {
                return Error("仅图片流程支持初始图；请使用不超过 5 MB 的 PNG、JPEG 或 WebP。", "invalid_reference");
            }
            int refWidth = 0, refHeight = 0;
            try
            {
                SwarmUI.Media.ImageFile decoded = SwarmUI.Media.ImageFile.FromDataString(reference);
                (refWidth, refHeight) = decoded.GetResolution();
                if (refWidth > 8192 || refHeight > 8192 || (long)refWidth * refHeight > 40_000_000)
                {
                    return Error("初始图尺寸过大，请缩小到最长边 8192 像素以内且不超过 4000 万像素。", "invalid_reference");
                }
            }
            catch (Exception)
            {
                return Error("无法解码初始图，请重新导出为 PNG、JPEG 或 WebP。", "invalid_reference");
            }
            input["initimage"] = reference;
            // Img2img / inpaint / outpaint need a creativity value; upscale keeps it optional.
            if (mode is "img2img" or "inpaint" or "outpaint")
            {
                if (!CopyOptionalNumber(rawInput, input, "initimagecreativity", 0, 1, false) || input["initimagecreativity"] is null)
                {
                    return Error("请提供 0–1 的重绘幅度。", "invalid_parameters");
                }
            }
        }

        // ----- Mask (inpaint / outpaint) -----
        string mask = (string)rawInput["maskimage"];
        if (mode is "inpaint" or "outpaint")
        {
            if (string.IsNullOrEmpty(mask) || !ValidReference(mask)
                || !Integer(rawInput, "maskshrinkgrow", 0, 512, out int shrink)
                || !Integer(rawInput, "maskblur", 0, 64, out int blur))
            {
                return Error("局部重绘/扩图需要有效的蒙版，蒙版收缩 0–512、羽化 0–64。", "invalid_mask");
            }
            input["maskimage"] = mask;
            input["maskshrinkgrow"] = shrink;
            input["maskblur"] = blur;
            input["initimagerecompositemask"] = true;
        }

        // ----- Upscale -----
        if (mode == "upscale")
        {
            if (!CopyOptionalNumber(rawInput, input, "refinerupscale", 1.5, 4, false) || input["refinerupscale"] is null)
            {
                return Error("请提供 1.5–4 的放大倍数。", "invalid_parameters");
            }
            // Keep the base stage close to the supplied image, then refine at the larger size.
            input["initimagecreativity"] = 0.25;
        }

        // ----- LoRAs -----
        JToken loraToken = rawInput["loras"];
        if (loraToken is not null && loraToken.Type != JTokenType.Null)
        {
            JArray loras = null;
            JArray weights = null;
            string loraError = null;
            if (loraToken.Type != JTokenType.Array || !TryBuildLoras(loraToken as JArray, rawInput["loraweights"], LocalLoras(session), out loras, out weights, out loraError))
            {
                return Error(loraError ?? "LoRA 配置无效。", "invalid_loras");
            }
            if (loras.Count > 0)
            {
                input["loras"] = loras;
                input["loraweights"] = weights;
            }
        }

        // ----- Sampler / scheduler -----
        if (!MatchOption(rawInput, "sampler", ComfyUIBackendExtension.Samplers, out string samplerError))
        {
            return Error(samplerError, "invalid_sampler");
        }
        if (!MatchOption(rawInput, "scheduler", ComfyUIBackendExtension.Schedulers, out string schedulerError))
        {
            return Error(schedulerError, "invalid_scheduler");
        }

        if (mode == "video")
        {
            if (!Integer(rawInput, "text2videoframes", 1, 129, out int frames))
            {
                return Error("视频帧数须为 1–129。引擎可能按模型要求调整帧数。", "invalid_parameters");
            }
            input["text2videoframes"] = frames;
            input["videoformat"] = "h264-mp4";
            if (!CopyOptionalNumber(rawInput, input, "videofps", 1, 60, true))
            {
                return Error("视频帧率须为 1–60，或留空以使用引擎默认值。", "invalid_parameters");
            }
        }
        try
        {
            JObject result = await T2IAPI.GenerateText2Image(session, images, input);
            if (result.ContainsKey("error"))
            {
                return GenerationError(result, false);
            }
            return result;
        }
        catch (Exception)
        {
            // No exception text in browser responses: upstream messages can contain paths or backend credentials.
            return Error("无法确认引擎执行结果。请在原生引擎核对队列和个人历史后再重试。", "generation_uncertain");
        }
    }

    /// <summary>Allows the stored-workflow selector only for a published workflow in an EcomCraft-local request.</summary>
    public static bool PublishedWorkflowPermissionOverride(string parameterName, string value, T2IParamInput input)
    {
        if (!string.Equals(parameterName, "comfyuicustomworkflow", StringComparison.OrdinalIgnoreCase)
            || input?.SourceSession?.User is not User user
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !input.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start"
            || !input.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested) || requested?.ToString() != value)
        {
            return false;
        }
        return TryGetPublishedWorkflow(value, out _, out _, out _);
    }

    /// <summary>Provides the saved parameter schema only for an approved workflow; arbitrary client-supplied schemas are ignored.</summary>
    public static T2IParamType PublishedWorkflowDynamicParamProvider(string name, T2IParamInput context)
    {
        if (context?.SourceSession?.User is not User user
            || user.HasPermission(ComfyUIBackendExtension.PermDynamicCustomWorkflows)
            || !user.HasPermission(EcomCraft.PermUsePublishedWorkflows)
            || !context.ExtraMeta.TryGetValue(LocalScopeKey, out object scope) || scope?.ToString() != "local_self_start"
            || !context.ExtraMeta.TryGetValue(PublishedWorkflowScopeKey, out object requested)
            || !TryGetPublishedWorkflow(requested?.ToString(), out _, out JObject metadata, out JObject savedValues))
        {
            return null;
        }
        if (string.Equals(name, "comfyworkflowparammetadata", StringComparison.OrdinalIgnoreCase))
        {
            return ComfyUIBackendExtension.FakeParameterMetadata;
        }
        if (WorkflowDeniedParameterIds.Contains(name) || WorkflowFormParameterIds.Contains(name))
        {
            return null;
        }
        JArray exposedFields = PublishedWorkflowInputs(context.SourceSession, metadata, savedValues, out bool supported);
        if (!supported || !exposedFields.OfType<JObject>().Any(field =>
            string.Equals(field["id"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        if (metadata.TryGetValue(name, out JToken parameter) && parameter is JObject parameterObject)
        {
            return T2IParamType.FromNet(parameterObject);
        }
        return null;
    }

    private static JArray PublishedWorkflowCatalog(Session session)
    {
        JArray published = new();
        if (session?.User is null || !session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return published;
        }
        foreach (string name in ComfyUIBackendExtension.CustomWorkflows.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetPublishedWorkflow(name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
                out JObject metadata, out JObject savedValues))
            {
                continue;
            }
            JArray fields = PublishedWorkflowInputs(session, metadata, savedValues, out bool supported);
            if (!supported)
            {
                continue;
            }
            published.Add(new JObject()
            {
                ["id"] = workflow.Name,
                ["name"] = workflow.Name,
                ["kind"] = InferWorkflowKind(workflow),
                ["description"] = workflow.Description ?? "",
                ["inputs"] = fields
            });
        }
        return published;
    }

    private static JArray PublishedWorkflowInputs(Session session, JObject metadata, JObject savedValues, out bool supported)
    {
        JArray fields = new();
        supported = true;
        foreach (JProperty property in metadata.Properties())
        {
            if (property.Value is not JObject definition || WorkflowDeniedParameterIds.Contains(property.Name)
                || WorkflowFormParameterIds.Contains(property.Name))
            {
                continue;
            }
            if (definition["visible"] is not null && bool.TryParse(definition["visible"].ToString(), out bool visible) && !visible)
            {
                continue;
            }
            string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
            if (!SupportedWorkflowInputTypes.Contains(type))
            {
                continue; // Keep fixed/hidden values from the saved template, but never expose unknown input types to the browser.
            }
            if (type == "dropdown")
            {
                if (definition["values"] is not JArray dropdownValues || dropdownValues.Count == 0)
                {
                    supported = false;
                    return fields;
                }
            }
            JToken defaultValue = savedValues[property.Name]?.DeepClone() ?? definition["default"]?.DeepClone();
            if (type == "image")
            {
                // Never disclose a workflow owner's default image data or their private output path to other users.
                defaultValue = new JValue("");
            }
            JObject field = new()
            {
                ["id"] = property.Name,
                ["name"] = definition["name"]?.ToString() ?? property.Name,
                ["type"] = type,
                ["description"] = definition["description"]?.ToString() ?? "",
                ["default"] = defaultValue,
                ["min"] = definition["min"]?.DeepClone() ?? new JValue(0),
                ["max"] = definition["max"]?.DeepClone() ?? new JValue(0),
                ["step"] = definition["step"]?.DeepClone() ?? new JValue(1),
                ["subtype"] = definition["subtype"]?.DeepClone()
            };
            if (type == "model")
            {
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler))
                {
                    List<string> allowedModels = handler.ListModelNamesFor(session);
                    if (allowedModels.Count == 0)
                    {
                        supported = false;
                        return fields;
                    }
                    field["values"] = new JArray(allowedModels);
                }
                else
                {
                    supported = false;
                    return fields;
                }
            }
            else if (definition["values"] is not null)
            {
                field["values"] = definition["values"].DeepClone();
            }
            fields.Add(field);
        }
        return fields;
    }

    private static string InferWorkflowKind(ComfyUIBackendExtension.ComfyCustomWorkflow workflow)
    {
        JObject graph = ParseWorkflowObject(workflow?.Prompt);
        bool containsVideoNode = graph?.Properties().Any(property =>
        {
            string classType = property.Value is JObject node ? node["class_type"]?.ToString() ?? "" : "";
            return classType.Contains("video", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("VHS_", StringComparison.OrdinalIgnoreCase)
                || classType.Contains("AnimateDiff", StringComparison.OrdinalIgnoreCase);
        }) == true;
        return containsVideoNode ? "video" : "image";
    }

    private static bool TryGetPublishedWorkflow(string name, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
        out JObject metadata, out JObject savedValues)
    {
        workflow = null;
        metadata = null;
        savedValues = new JObject();
        if (string.IsNullOrWhiteSpace(name) || !ComfyUIBackendExtension.CustomWorkflows.ContainsKey(name))
        {
            return false;
        }
        workflow = ComfyUIBackendExtension.GetWorkflowByName(name);
        if (workflow is null || !workflow.EnableInSimple || string.IsNullOrWhiteSpace(workflow.Prompt))
        {
            workflow = null;
            return false;
        }
        metadata = ParseWorkflowObject(workflow.CustomParams);
        savedValues = ParseWorkflowObject(workflow.ParamValues) ?? new JObject();
        if (metadata is null || ParseWorkflowObject(workflow.Prompt) is null)
        {
            workflow = null;
            return false;
        }
        return true;
    }

    private static JObject ParseWorkflowObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            return JToken.Parse(json) as JObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<JObject> GeneratePublishedWorkflow(Session session, JObject rawInput, string workflowId)
    {
        if (!session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows))
        {
            return Error("此账号没有使用已发布工作流的权限。", "bad_permissions");
        }
        // ComfyUI backend refreshes clear the in-memory workflow registry, so restore native entries before lookup.
        ImportNativeComfyWorkflows();
        if (!TryGetPublishedWorkflow(workflowId, out ComfyUIBackendExtension.ComfyCustomWorkflow workflow,
            out JObject metadata, out JObject savedValues))
        {
            return Error("工作流不存在、未发布到简单工作台，或工作流配置无效。请刷新工作流列表。", "invalid_workflow");
        }
        string kind = (string)rawInput["kind"];
        if (kind is not ("image" or "video"))
        {
            return Error("未知的创作类型。", "invalid_mode");
        }
        if (kind == "video" && !session.User.HasPermission(Permissions.ParamVideo))
        {
            return Error("此账号没有视频生成权限。", "bad_permissions");
        }
        if (kind != InferWorkflowKind(workflow))
        {
            return Error("所选工作流与当前创作类型不匹配，请切换图片/视频类型或选择其他工作流。", "invalid_mode");
        }
        string modelId = (string)rawInput["model"];
        T2IModel model = LocalModels(session).FirstOrDefault(m => m.Name == modelId && Kind(m) == kind);
        if (model is null)
        {
            return Error("模型不可用、账号无权访问，或模型类型与当前创作类型不匹配。", "invalid_model");
        }
        if (!Program.Backends.EnumerateT2IBackends.Any(IsLocalAvailable))
        {
            return Error("没有可用的本机自启动 ComfyUI 节点；不会转发到远程或云端。", "local_unavailable");
        }
        string prompt = (string)rawInput["prompt"];
        string negative = (string)rawInput["negativeprompt"] ?? "";
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 12000 || negative.Length > 6000)
        {
            return Error("请填写创作描述（最多 12000 字符），排除内容最多 6000 字符。", "invalid_prompt");
        }
        if (new[] { prompt, negative }.Any(p => p.Contains('<') || p.Contains('>') || p.Contains("__")))
        {
            return Error("本版工作台只接受普通描述，不支持原生 <…> 指令、预设或 __通配符__。", "unsupported_prompt_syntax");
        }
        int step = Math.Max(8, model.ModelClass.CompatClass.ResolutionPrecision);
        int imageLimit = kind == "video" ? 1 : 4;
        if (!Integer(rawInput, "width", 256, 2048, out int width) || width % step != 0
            || !Integer(rawInput, "height", 256, 2048, out int height) || height % step != 0
            || !Integer(rawInput, "images", 1, imageLimit, out int images))
        {
            return Error($"尺寸须为 256–2048 且是 {step} 的倍数；图片每批 1–4 张，视频每批 1 条。", "invalid_parameters");
        }
        JToken rawWorkflowParams = rawInput["workflow_params"];
        if (rawWorkflowParams is not null && rawWorkflowParams.Type is not (JTokenType.Object or JTokenType.Null))
        {
            return Error("工作流参数格式无效。", "invalid_workflow_parameters");
        }
        JObject requestedParams = rawWorkflowParams as JObject ?? new JObject();
        if (requestedParams.Count > 60)
        {
            return Error("工作流参数数量超过限制。", "invalid_workflow_parameters");
        }
        JArray publishedFields = PublishedWorkflowInputs(session, metadata, savedValues, out bool workflowInputsSupported);
        if (!workflowInputsSupported)
        {
            return Error("该工作流包含暂不支持的网页输入定义，请在 SwarmUI 工作流编辑器中修正后重新发布。", "invalid_workflow_parameters");
        }
        HashSet<string> allowedWorkflowInputIds = new(publishedFields.OfType<JObject>()
            .Select(field => field["id"]?.ToString()).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        JObject input = new()
        {
            // This must precede dynamic input IDs so SwarmUI recognizes them as parameters of this stored workflow.
            ["comfyuicustomworkflow"] = workflow.Name
        };
        foreach (JProperty property in savedValues.Properties())
        {
            if (!metadata.TryGetValue(property.Name, out JToken savedDefinition)
                || savedDefinition is not JObject savedDefinitionObject
                || WorkflowDeniedParameterIds.Contains(property.Name))
            {
                continue;
            }
            JToken value = property.Value;
            string type = savedDefinitionObject["type"]?.ToString()?.ToLowerInvariant() ?? "";
            // A published workflow may contain an owner's local image path or image data. It is a template,
            // not a shared asset, so require each user to provide image inputs explicitly.
            if (value.Type == JTokenType.Null || type == "image")
            {
                continue;
            }
            if (SupportedWorkflowInputTypes.Contains(type)
                && !ValidatePublishedWorkflowInput(session, property.Name, value, savedDefinitionObject, out _))
            {
                continue;
            }
            input[property.Name] = value.DeepClone();
        }
        input["comfyworkflowparammetadata"] = workflow.CustomParams;
        foreach (JProperty property in requestedParams.Properties())
        {
            if (!allowedWorkflowInputIds.Contains(property.Name)
                || WorkflowFormParameterIds.Contains(property.Name) || WorkflowDeniedParameterIds.Contains(property.Name)
                || !metadata.TryGetValue(property.Name, out JToken definition) || definition is not JObject definitionObject)
            {
                return Error($"工作流参数“{property.Name}”不在此工作流的允许输入列表中。", "invalid_workflow_parameters");
            }
            if (!ValidatePublishedWorkflowInput(session, property.Name, property.Value, definitionObject, out string validationError))
            {
                return Error(validationError, "invalid_workflow_parameters");
            }
            string type = definitionObject["type"]?.ToString()?.ToLowerInvariant() ?? "";
            if (type == "image" && string.IsNullOrWhiteSpace(property.Value.ToString()))
            {
                input.Remove(property.Name);
            }
            else
            {
                input[property.Name] = property.Value.DeepClone();
            }
        }
        // Build the root request from a strict allowlist; never forward arbitrary client-supplied routes, metadata or workflow JSON.
        input["model"] = model.Name;
        input["prompt"] = prompt;
        input["negativeprompt"] = negative;
        input["width"] = width;
        input["height"] = height;
        input["images"] = images;
        input["webhooks"] = "None";
        input["donotsave"] = false;
        if (!CopyOptionalNumber(rawInput, input, "seed", -1, uint.MaxValue, true)
            || !CopyOptionalNumber(rawInput, input, "steps", 1, 100, true)
            || !CopyOptionalNumber(rawInput, input, "cfgscale", 0, 30, false))
        {
            return Error("种子、步数或提示词强度不在允许范围内。", "invalid_parameters");
        }
        if (!MatchOption(rawInput, "sampler", ComfyUIBackendExtension.Samplers, out string samplerError))
        {
            return Error(samplerError, "invalid_sampler");
        }
        if (!MatchOption(rawInput, "scheduler", ComfyUIBackendExtension.Schedulers, out string schedulerError))
        {
            return Error(schedulerError, "invalid_scheduler");
        }
        if (kind == "video")
        {
            if (!Integer(rawInput, "text2videoframes", 1, 129, out int frames))
            {
                return Error("视频帧数须为 1–129。", "invalid_parameters");
            }
            input["text2videoframes"] = frames;
            input["videoformat"] = "h264-mp4";
            if (!CopyOptionalNumber(rawInput, input, "videofps", 1, 60, true))
            {
                return Error("视频帧率须为 1–60，或留空以使用引擎默认值。", "invalid_parameters");
            }
        }
        input["extra_metadata"] = new JObject()
        {
            [LocalScopeKey] = "local_self_start",
            [PublishedWorkflowScopeKey] = workflow.Name
        };
        try
        {
            JObject result = await T2IAPI.GenerateText2Image(session, images, input);
            if (result.ContainsKey("error"))
            {
                return GenerationError(result, true);
            }
            return result;
        }
        catch (Exception)
        {
            return Error("无法确认工作流执行结果。请在原生引擎核对队列和个人历史后再重试。", "generation_uncertain");
        }
    }

    private static bool ValidatePublishedWorkflowInput(Session session, string id, JToken value, JObject definition, out string error)
    {
        error = null;
        string type = definition["type"]?.ToString()?.ToLowerInvariant() ?? "";
        if (value.Type == JTokenType.Null)
        {
            return true;
        }
        string raw = value.Type == JTokenType.String ? value.Value<string>() ?? "" : value.ToString();
        double min = ReadWorkflowBound(definition["min"]);
        double max = ReadWorkflowBound(definition["max"]);
        bool hasBounds = min != 0 || max != 0;
        switch (type)
        {
            case "text":
                if (value.Type != JTokenType.String || raw.Length > 12000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流参数“{id}”必须是普通文本，不能超过 12000 个字符，也不能包含原生指令语法。";
                    return false;
                }
                return true;
            case "integer":
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integerValue)
                    || (hasBounds && (integerValue < min || integerValue > max)))
                {
                    error = $"工作流参数“{id}”的整数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "decimal":
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalValue)
                    || !double.IsFinite(decimalValue) || (hasBounds && (decimalValue < min || decimalValue > max)))
                {
                    error = $"工作流参数“{id}”的数值无效或超出允许范围。";
                    return false;
                }
                return true;
            case "boolean":
                if (!bool.TryParse(raw, out _))
                {
                    error = $"工作流参数“{id}”必须是布尔值。";
                    return false;
                }
                return true;
            case "dropdown":
                if (value.Type != JTokenType.String || definition["values"] is not JArray choices || choices.Count == 0
                    || !choices.Any(choice => choice.ToString() == raw || choice.ToString().Before("///") == raw))
                {
                    error = $"工作流参数“{id}”不是当前工作流允许的选项。";
                    return false;
                }
                return true;
            case "model":
                string subtype = definition["subtype"]?.ToString() ?? "Stable-Diffusion";
                if (value.Type != JTokenType.String || !Program.T2IModelSets.TryGetValue(subtype, out T2IModelHandler handler)
                    || T2IParamTypes.GetBestModelInList(raw, handler.ListModelNamesFor(session)) is null)
                {
                    error = $"工作流参数“{id}”引用了不存在或当前账号无权访问的模型。";
                    return false;
                }
                return true;
            case "image":
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return true;
                }
                if (raw.StartsWith("inputs/", StringComparison.Ordinal) || raw.StartsWith("raw/", StringComparison.Ordinal)
                    || raw.StartsWith("Starred/", StringComparison.Ordinal))
                {
                    return true; // SwarmUI validates and resolves these within the user's own output directory.
                }
                if (!ValidReference(raw))
                {
                    error = $"工作流图片参数“{id}”必须是 PNG、JPEG 或 WebP，且不超过 5 MB。";
                    return false;
                }
                try
                {
                    SwarmUI.Media.ImageFile decoded = SwarmUI.Media.ImageFile.FromDataString(raw);
                    (int imageWidth, int imageHeight) = decoded.GetResolution();
                    if (imageWidth > 8192 || imageHeight > 8192 || (long)imageWidth * imageHeight > 40_000_000)
                    {
                        error = $"工作流图片参数“{id}”尺寸过大。";
                        return false;
                    }
                }
                catch (Exception)
                {
                    error = $"无法解码工作流图片参数“{id}”。";
                    return false;
                }
                return true;
            case "list":
                if (value.Type != JTokenType.String || raw.Length > 4000 || raw.Contains('<') || raw.Contains('>') || raw.Contains("__"))
                {
                    error = $"工作流列表参数“{id}”格式无效，或包含不支持的原生指令语法。";
                    return false;
                }
                return true;
            default:
                error = $"工作流参数“{id}”的数据类型“{type}”暂不支持由网页修改，请调整工作流的默认值或输入类型。";
                return false;
        }
    }

    private static double ReadWorkflowBound(JToken token)
    {
        return token is not null && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value) ? value : 0;
    }

    private static bool TryBuildLoras(JArray loraArray, JToken weightToken, List<string> allowed, out JArray loras, out JArray weights, out string error)
    {
        loras = new JArray();
        weights = new JArray();
        error = null;
        if (loraArray.Count > MaxLoras)
        {
            error = $"LoRA 最多 {MaxLoras} 个。";
            return false;
        }
        double[] providedWeights = new double[loraArray.Count];
        if (weightToken is not null && weightToken.Type != JTokenType.Null)
        {
            if (weightToken.Type != JTokenType.Array || weightToken.Count() != loraArray.Count)
            {
                error = "LoRA 权重数量必须与 LoRA 数量一致。";
                return false;
            }
            for (int i = 0; i < loraArray.Count; i++)
            {
                if (!double.TryParse(weightToken[i]?.ToString(), out double w) || !double.IsFinite(w) || w < -10 || w > 10)
                {
                    error = "LoRA 权重须为 -10 到 10 的数字。";
                    return false;
                }
                providedWeights[i] = w;
            }
        }
        HashSet<string> seen = new();
        for (int i = 0; i < loraArray.Count; i++)
        {
            string name = loraArray[i]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || !allowed.Contains(name) || !seen.Add(name))
            {
                error = "LoRA 不存在、无权限或重复选择，请刷新后重试。";
                return false;
            }
            loras.Add(name);
            weights.Add(weightToken is null ? "1" : providedWeights[i].ToString());
        }
        return true;
    }

    private static bool MatchOption(JObject source, string key, List<string> allowed, out string error)
    {
        error = null;
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null || token.ToString() == "")
        {
            return true;
        }
        string value = token.ToString();
        HashSet<string> ids = new(allowed.Select(v => v.Before("///")));
        if (!ids.Contains(value))
        {
            error = $"{key} 取值不受支持，请使用列表中的选项。";
            return false;
        }
        return true;
    }

    private static bool Integer(JObject input, string key, int min, int max, out int value)
    {
        value = 0;
        return input[key]?.Type == JTokenType.Integer && int.TryParse(input[key].ToString(), out value) && value >= min && value <= max;
    }

    private static bool CopyOptionalNumber(JObject source, JObject target, string key, double min, double max, bool integral)
    {
        JToken token = source[key];
        if (token is null || token.Type == JTokenType.Null)
        {
            return true;
        }
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            return false;
        }
        double value = token.Value<double>();
        if (!double.IsFinite(value) || value < min || value > max || (integral && value != Math.Truncate(value)))
        {
            return false;
        }
        target[key] = token.DeepClone();
        return true;
    }

    private static bool ValidReference(string data)
    {
        int comma = data.IndexOf(',');
        if (comma < 0 || comma > 30 || data.Length > (MaxReferenceBytes + 2) / 3 * 4 + 40
            || data[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64"))
        {
            return false;
        }
        byte[] buffer = new byte[(data.Length - comma) * 3 / 4];
        return Convert.TryFromBase64String(data[(comma + 1)..], buffer, out int count) && count > 0 && count <= MaxReferenceBytes;
    }

    private static JObject GenerationError(JObject result, bool workflow)
    {
        string detail = result["error"]?.ToString() ?? "";
        string requestId = result["request_id"]?.ToString();
        Logs.Warning($"EcomCraft {(workflow ? "workflow" : "standard")} generation failed{(string.IsNullOrWhiteSpace(requestId) ? "" : $" (request {requestId})")}: {detail}");
        string message = detail switch
        {
            "Timeout! All backends are occupied with other tasks." => "本机 ComfyUI 节点等待超时，请检查后端状态和队列。",
            "Generation session interrupted." => "生成任务已被中断，请在原生引擎核对队列和个人历史。",
            "No images were generated (all refused, or failed - check server logs for details)." => "引擎没有生成可保存的结果，请检查工作流输出节点、模型依赖和服务器日志。",
            "Server failed to save an image." => "引擎生成了结果，但服务器保存文件失败，请检查输出目录和磁盘空间。",
            _ => $"引擎未完整返回结果。请在原生引擎检查{(workflow ? "工作流" : "模型")}依赖、参数和个人历史；可能已有部分输出，请勿直接重复提交。"
        };
        Match failedNode = Regex.Match(detail, @"^ComfyUI execution error \(Node ([0-9]+): ([A-Za-z0-9_]+)\):");
        if (failedNode.Success)
        {
            message = $"ComfyUI 节点 {failedNode.Groups[1].Value}（{failedNode.Groups[2].Value}）执行失败。请在原生工作流中检查该节点及其模型依赖，并核对个人历史。";
        }
        if (detail.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            message = "生成时显存不足。请在原生引擎核对任务结果，并尝试减小尺寸或使用更轻量的模型。";
        }
        if (detail.Contains("miopenStatusInternalError", StringComparison.OrdinalIgnoreCase))
        {
            message = "本机 ROCm/MIOpen 运行失败。请检查 ComfyUI 进程对 MIOpen 缓存目录的访问权限，并查看服务器日志中的原始错误。";
        }
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            message += $" 请求编号：{requestId}。";
        }
        return Error(message, "generation_failed");
    }

    private static JObject Error(string text, string id) => new() { ["error"] = text, ["error_id"] = id };
}
