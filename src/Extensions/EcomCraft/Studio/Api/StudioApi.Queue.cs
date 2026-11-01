using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using EcomCraftStudio.MagicPrompt.WebAPI;
using EcomCraftStudio.Studio.Generation;
using EcomCraftStudio.Studio.Storage;
using EcomCraftStudio.Studio.Templates;
using EcomCraftStudio.Studio.Prompting;
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


/// <summary>Queue and cancel API routes. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    [API.APIDescription("List ComfyUI queue entries on registered execution servers and active SwarmUI generation claims without disclosing prompts, addresses or session IDs. "
        + "The current account's own tasks also carry a time estimate (F70): estimate_seconds, remaining_seconds and start_in_seconds, null before the server has a sample.", "backends: [], native_tasks: [], tasks: []")]
    public static async Task<JObject> EcomCraftStudioQueue(Session session)
    {
        if (!CanView(session))
        {
            return Error("无权查看任务队列。", "bad_permissions");
        }
        JArray backends = new();
        JArray nativeTasks = new();
        HashSet<string> activeProgress = new();
        List<BackendHandler.T2IBackendData> servers = [.. ExecutionServers.All];
        JObject[] queues = await Task.WhenAll(servers.Select(ExecutionScheduler.ReadComfyQueue));
        for (int i = 0; i < servers.Count; i++)
        {
            BackendHandler.T2IBackendData data = servers[i];
            JObject queue = queues[i];
            string name = ExecutionServers.DisplayName(data);
            JObject item = new()
            {
                ["id"] = data.ID,
                ["name"] = name,
                ["status"] = data.Backend.Status.ToString().ToLowerFast(),
                ["running"] = JValue.CreateNull(),
                ["pending"] = JValue.CreateNull(),
                ["assigned"] = 0,
                ["queue"] = JValue.CreateNull(),
                ["load"] = JValue.CreateNull(),
                ["busy"] = false
            };
            if (data.Backend.Status == BackendStatus.RUNNING && queue is null)
            {
                item["status"] = "unresponsive";
            }
            if (queue is not null)
            {
                ExecutionScheduler.Load load = ExecutionScheduler.LoadOf(data, queue);
                item["running"] = load.Running;
                item["pending"] = load.Pending;
                item["assigned"] = load.Assigned;
                item["queue"] = load.Queue;
                item["load"] = load.State;
                item["busy"] = load.State == "congested";
                foreach (JArray entry in new[] { queue["queue_running"] as JArray, queue["queue_pending"] as JArray }.Where(array => array is not null).SelectMany(array => array.OfType<JArray>()))
                {
                    if (entry.Count > 1 && entry[1]?.Type == JTokenType.String)
                    {
                        activeProgress.Add($"{data.ID}:{entry[1]}");
                    }
                }
                foreach (JToken task in NativeQueueEntries(queue["queue_running"] as JArray, data.ID, name, true))
                {
                    nativeTasks.Add(task);
                }
                foreach (JToken task in NativeQueueEntries(queue["queue_pending"] as JArray, data.ID, name, false))
                {
                    nativeTasks.Add(task);
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
                JObject task = new()
                {
                    ["id"] = mine ? claim.ID : null,
                    ["mine"] = mine,
                    ["state"] = claim.ShouldCancel ? "stopping" : live > 0 ? "running" : backendWait > 0 ? "waiting_backend" : "queued",
                    ["count"] = Math.Max(1, waiting),
                    ["elapsed_seconds"] = Math.Max(0, (Environment.TickCount64 - claim.StartTime) / 1000),
                    ["request_id"] = mine && claim.UserRequestId > 0 ? claim.UserRequestId : null
                };
                // Only the owner's own tasks carry a time estimate (F70); it holds seconds only.
                if (mine && TaskTiming.ForClaim(claim.ID) is TaskTiming.Scheduled timing)
                {
                    TaskTiming.AddEstimate(task, timing, live > 0);
                }
                tasks.Add(task);
            }
        }
        // Results of this account's recent background tasks (R6); other accounts' results are never listed.
        JArray finished = new();
        foreach (TaskOutcomes.Outcome outcome in TaskOutcomes.ForUser(session.User.UserID))
        {
            finished.Add(new JObject()
            {
                ["id"] = outcome.TaskId,
                ["state"] = outcome.Failed ? "failed" : "done",
                ["error"] = outcome.Error,
                ["error_id"] = outcome.ErrorId,
                ["execution_server"] = outcome.Server,
                ["images"] = new JArray(outcome.Images ?? [])
            });
        }
        JObject queueResult = new() { ["backends"] = backends, ["native_tasks"] = nativeTasks, ["tasks"] = tasks, ["finished_tasks"] = finished };
        // Administrators see the disk-space warning on the queue page (F46); ordinary accounts get nothing extra.
        if (session.User.HasPermission(Permissions.EditServerSettings))
        {
            queueResult["disk"] = StorageApi.DiskJson();
        }
        return queueResult;
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
}
