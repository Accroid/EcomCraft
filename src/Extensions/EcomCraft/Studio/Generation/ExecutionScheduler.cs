using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Chooses the executor for an EcomCraft request with Rule 1 and counts tasks the platform assigned but has not yet handed to ComfyUI (R5.2).
/// A chosen executor is pinned through <see cref="StudioApi.ExecutionServerKey"/>; SwarmUI then only runs the request there and waits while it is busy.</summary>
public static class ExecutionScheduler
{
    /// <summary>Upper bound for reading one server's ComfyUI queue while scheduling or listing the queue.</summary>
    private static readonly TimeSpan QueueReadTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Images assigned by this platform and not yet finished, by execution server ID.</summary>
    private static readonly ConcurrentDictionary<int, int> InFlight = new();

    /// <summary>One server's load: ComfyUI running and pending counts (null when unreadable), tasks assigned but not yet in ComfyUI, and the R5.2 queue.</summary>
    public sealed record class Load(int? Running, int? Pending, int Assigned, int Queue)
    {
        /// <summary>R5.3 state: "idle", "working" (running, nothing waiting) or "congested" (something waiting); null when the queue is unreadable.</summary>
        public string State => Running is null ? null : Queue - Running.Value > 0 ? "congested" : Running.Value > 0 ? "working" : "idle";
    }

    /// <summary>Releases an in-flight count once.</summary>
    private sealed class Assignment(int serverId, int count) : IDisposable
    {
        /// <summary>Set after the first release.</summary>
        private int Released;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref Released, 1) == 0)
            {
                InFlight.AddOrUpdate(serverId, 0, (_, current) => Math.Max(0, current - count));
            }
        }
    }

    /// <summary>Counts a batch as assigned to a server until the returned object is disposed, which must happen when the generation ends for any reason.</summary>
    public static IDisposable Track(int serverId, int count)
    {
        InFlight.AddOrUpdate(serverId, count, (_, current) => current + count);
        return new Assignment(serverId, count);
    }

    /// <summary>Reads one execution server's ComfyUI queue; null when the server is not running or does not answer in time.</summary>
    public static async Task<JObject> ReadComfyQueue(BackendHandler.T2IBackendData data)
    {
        if (data.Backend.Status != BackendStatus.RUNNING || data.Backend is not ComfyUIAPIAbstractBackend backend)
        {
            return null;
        }
        try
        {
            using CancellationTokenSource timeout = new(QueueReadTimeout);
            using HttpResponseMessage response = await ComfyUIAPIAbstractBackend.HttpClient.GetAsync($"{backend.APIAddress}/queue", timeout.Token);
            response.EnsureSuccessStatusCode();
            return JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Computes a server's load from its ComfyUI queue (may be null). Assigned tasks already claimed by SwarmUI are assumed to be in the ComfyUI queue,
    /// so only the remainder is added; tasks claimed but not yet posted to ComfyUI are briefly not counted.</summary>
    public static Load LoadOf(BackendHandler.T2IBackendData data, JObject queue)
    {
        int assigned = Math.Max(0, (InFlight.TryGetValue(data.ID, out int inFlight) ? inFlight : 0) - data.Usages);
        if (queue is null)
        {
            return new Load(null, null, assigned, assigned);
        }
        int running = (queue["queue_running"] as JArray)?.Count ?? 0;
        int pending = (queue["queue_pending"] as JArray)?.Count ?? 0;
        return new Load(running, pending, assigned, running + pending + assigned);
    }

    /// <summary>Applies Rule 1 to the registered executors. A server is online when it is ready and its ComfyUI queue answers.
    /// <paramref name="capability"/> returns whether an online server can run the request and, if not, what it lacks.</summary>
    public static async Task<SchedulerRules.Decision> PlanAsync(Func<BackendHandler.T2IBackendData, List<string>> capability, int? pinnedId)
    {
        List<Executor> executors = [.. ExecutorRegistry.Current().Where(executor => executor.Data is not null)];
        JObject[] queues = await Task.WhenAll(executors.Select(executor => ExecutionServers.IsReady(executor.Data)
            ? ReadComfyQueue(executor.Data) : Task.FromResult<JObject>(null)));
        List<SchedulerRules.Candidate> candidates = [];
        for (int i = 0; i < executors.Count; i++)
        {
            BackendHandler.T2IBackendData data = executors[i].Data;
            bool online = queues[i] is not null && ExecutionServers.IsReady(data);
            List<string> problems = online ? capability(data) ?? [] : [];
            candidates.Add(new SchedulerRules.Candidate(data.ID, executors[i].Name, online, online && problems.Count == 0, problems,
                LoadOf(data, queues[i]).Queue, data.TimeLastRelease));
        }
        return SchedulerRules.Decide(candidates, pinnedId);
    }
}
