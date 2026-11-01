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


/// <summary>ComfyUI execution-server progress observation for the queue view. Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
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

    private static JArray NativeQueueEntries(JArray entries, int backendId, string backendName, bool running)
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
                ["backend_name"] = backendName,
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
}
