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


/// <summary>Submissions whose prompt is written in the background (F56). Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    /// <summary>Most submissions one account may have waiting for their prompt at the same time (F56).</summary>
    private const int MaxPreparingPerUser = 5;

    /// <summary>How long a submission waiting for its prompt, or its answer, is kept for its owner (F56).</summary>
    private const long PreparingKeepMs = 10 * 60 * 1000;

    /// <summary>Submissions whose prompt is written in the background (F56), by preparation ID.</summary>
    private static readonly ConcurrentDictionary<string, PreparingTask> Preparing = new();

    /// <summary>One submission waiting for its prompt (F56): its owner and, once the prompt is written and the generation submitted, the submit answer.
    /// Nothing but that answer for the owner is kept, and it is dropped after <see cref="PreparingKeepMs"/>.</summary>
    private sealed class PreparingTask(string userId)
    {
        /// <summary>Owner account.</summary>
        public readonly string UserId = userId;

        /// <summary>Protects the fields below.</summary>
        private readonly object Lock = new();

        /// <summary>When the task started or ended, <see cref="Environment.TickCount64"/>.</summary>
        private long Changed = Environment.TickCount64;

        /// <summary>When the task started, <see cref="Environment.TickCount64"/>, for the prompt time estimate (F70).</summary>
        public readonly long Started = Environment.TickCount64;

        /// <summary>The submit answer (task ID, execution server, prompt fields, or a sanitized error); null while the prompt is written.</summary>
        private JObject Answer;

        /// <summary>The submit answer, or null while the prompt is still being written.</summary>
        public JObject Result { get { lock (Lock) { return Answer; } } }

        /// <summary>Stores the submit answer.</summary>
        public void Finish(JObject answer)
        {
            lock (Lock)
            {
                Answer = answer;
                Changed = Environment.TickCount64;
            }
        }

        /// <summary>Whether the entry has been kept long enough.</summary>
        public bool Expired(long now)
        {
            lock (Lock)
            {
                return now - Changed > PreparingKeepMs;
            }
        }
    }

    /// <summary>Drops expired preparation entries.</summary>
    private static void PrunePreparing()
    {
        long now = Environment.TickCount64;
        foreach (KeyValuePair<string, PreparingTask> pair in Preparing.Where(pair => pair.Value.Expired(now)).ToArray())
        {
            Preparing.TryRemove(pair.Key, out _);
        }
    }

    /// <summary>How many of an account's submissions are still waiting for their prompt.</summary>
    private static int PreparingCount(string userId)
    {
        PrunePreparing();
        return Preparing.Values.Count(task => task.UserId == userId && task.Result is null);
    }

    /// <summary>Answers a template submission at once and writes its prompt in the background (F56); when the prompt is ready the already resolved request
    /// is completed with it and submitted to the scheduler as usual. The language model keeps its own 45-second limit and falls back to the defaults,
    /// so generation is never blocked by it. The owner reads the outcome through <see cref="EcomCraftStudioPrepareStatus"/>.</summary>
    private static JObject StartPreparing(Session session, JObject resolved, string templateId, int? pinnedServer, string preserveToken, Func<JObject, Task> postProcess,
        Func<Task<PromptService.Composed>> compose)
    {
        string id = Guid.NewGuid().ToString("N");
        PreparingTask task = new(session.User.UserID);
        Preparing[id] = task;
        _ = Utilities.RunCheckedTask(async () =>
        {
            JObject answer = null;
            try
            {
                PromptService.Composed composed = await compose();
                TaskTiming.RecordSince(TaskTiming.Compose, task.Started);
                resolved["prompt"] = TemplateApi.WithTemplatePrompt(templateId, composed.Prompt);
                answer = await GeneratePublishedWorkflow(session, resolved, resolved.Value<string>("workflow_id"), pinnedServer, templateId, postProcess);
                if (!answer.ContainsKey("error"))
                {
                    AddPromptFields(answer, composed);
                }
            }
            catch (Exception ex)
            {
                Logs.Warning($"EcomCraft background prompt task failed: {ex.GetType().Name}");
                answer = Error("无法确认任务是否已提交。请在队列和作品画廊核对后再重试。", "generation_uncertain");
            }
            finally
            {
                if (answer is null || answer.ContainsKey("error"))
                {
                    PostProcess.ProductPreserve.Discard(preserveToken);
                }
                task.Finish(answer ?? Error("无法确认任务是否已提交。请在队列和作品画廊核对后再重试。", "generation_uncertain"));
            }
        }, "ecomcraft prompt preparation");
        return new JObject() { ["queued"] = true, ["task_id"] = null, ["prepare_id"] = id, ["stage"] = "prompt" };
    }

    [API.APIDescription("Stage of one of the current account's submissions whose prompt is written in the background (F56): stage \"prompt\" while the prompt is written "
        + "(with elapsed_seconds, estimate_seconds and remaining_seconds, F70), then \"submitted\" with the usual submit answer (task_id, execution_server, images, prompt_used, prompt_notice_code…) or \"failed\" with reason and reason_id (the error text and code).", "stage: \"prompt\"")]
    public static Task<JObject> EcomCraftStudioPrepareStatus(Session session, string prepare_id = "")
    {
        if (!CanView(session))
        {
            return Task.FromResult(Error("无权查看任务。", "bad_permissions"));
        }
        PrunePreparing();
        if (string.IsNullOrEmpty(prepare_id) || !Preparing.TryGetValue(prepare_id, out PreparingTask task) || task.UserId != session.User.UserID)
        {
            return Task.FromResult(Error("任务不存在或不属于当前账号。", "task_not_found"));
        }
        JObject answer = task.Result;
        if (answer is null)
        {
            JObject waiting = new() { ["stage"] = "prompt" };
            TaskTiming.AddPromptEstimate(waiting, task.Started);
            return Task.FromResult(waiting);
        }
        if (answer.ContainsKey("error"))
        {
            // The failure is described in its own fields, so the status call itself still succeeds for the client.
            return Task.FromResult(new JObject() { ["stage"] = "failed", ["reason"] = answer["error"]?.ToString(), ["reason_id"] = answer["error_id"]?.ToString() });
        }
        JObject result = (JObject)answer.DeepClone();
        result["stage"] = "submitted";
        return Task.FromResult(result);
    }
}
