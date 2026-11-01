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


/// <summary>Background generation submission and outcome tracking (R6). Part of the StudioApi partial class (R07).</summary>
public static partial class StudioApi
{
    /// <summary>Tracks one background generation: its claim ID once seen, and its sanitized result once finished. The outcome is recorded when both are known.</summary>
    private sealed class BackgroundTask(string userId, string server)
    {
        /// <summary>Protects the fields below.</summary>
        private readonly object Lock = new();

        /// <summary>The SwarmUI claim ID, or 0 before it has been seen.</summary>
        private long TaskId;

        /// <summary>The SwarmUI claim ID, or 0 before it has been seen.</summary>
        public long ClaimId { get { lock (Lock) { return TaskId; } } }

        /// <summary>Whether the generation has ended.</summary>
        public bool Finished { get { lock (Lock) { return Done; } } }

        /// <summary>Backing flag for <see cref="Finished"/>.</summary>
        private bool Done;

        /// <summary>The sanitized error response, or null after success.</summary>
        public JObject Failure { get { lock (Lock) { return Error; } } }

        /// <summary>Backing value for <see cref="Failure"/>.</summary>
        private JObject Error;

        /// <summary>Whether the outcome has been recorded.</summary>
        private bool Recorded;

        /// <summary>Sets the claim ID seen by the submitting request.</summary>
        public void SetTaskId(long id)
        {
            lock (Lock)
            {
                TaskId = id;
                RecordIfReady();
            }
        }

        /// <summary>Result URLs of a successful generation.</summary>
        private List<string> Images = [];

        /// <summary>Result URLs of a successful generation, or an empty list.</summary>
        public List<string> ImageUrls { get { lock (Lock) { return [.. Images]; } } }

        /// <summary>Marks the generation ended with a sanitized error, or null on success with its result URLs.</summary>
        public void Finish(JObject error, List<string> images)
        {
            lock (Lock)
            {
                Done = true;
                Error = error;
                Images = images ?? [];
                RecordIfReady();
            }
        }

        /// <summary>Records the outcome once the claim ID and the result are both known. Callers hold <see cref="Lock"/>.</summary>
        private void RecordIfReady()
        {
            if (Recorded || !Done || TaskId <= 0)
            {
                return;
            }
            Recorded = true;
            TaskOutcomes.Record(new TaskOutcomes.Outcome(userId, TaskId, Error is not null, Error?["error"]?.ToString(), Error?["error_id"]?.ToString(),
                server, Environment.TickCount64, Images));
        }
    }

    /// <summary>Runs one studio generation in the background so the submitting browser stays usable; finished images are saved to the user's gallery as usual. The claim is watched for a few seconds so the caller can track the task id.
    /// The scheduler assignment is released when the generation ends. The result, success or a sanitized failure (R6.3), is kept briefly for the queue API;
    /// a failure that happens before the caller gets the task ID is returned directly.</summary>
    private static async Task<JObject> SubmitBackgroundGeneration(Session session, int images, JObject input, IDisposable assignment, bool workflow, string server,
        Func<JObject, Task> postProcess, bool candidates, TaskTiming.Scheduled timing)
    {
        HashSet<long> claimsBefore = [.. session.Claims.Keys];
        BackgroundTask task = new(session.User.UserID, server);
        _ = Utilities.RunCheckedTask(async () =>
        {
            JObject failure = null;
            List<string> outputs = [];
            try
            {
                JObject result = await T2IAPI.GenerateText2Image(session, images, input);
                if (result.ContainsKey("error"))
                {
                    failure = GenerationError(result, workflow);
                }
                else
                {
                    TaskTiming.Finished(timing);
                    if (postProcess is not null)
                    {
                        await postProcess(result);
                    }
                    WorkSync.EnqueueResult(session.User, result);
                    // Only saved files (View/... or Output/...), never inline data, are kept for the queue API.
                    outputs = [.. (result["images"] as JArray ?? new JArray()).Select(image => image.Type == JTokenType.String ? image.ToString() : image["image"]?.ToString())
                        .Where(url => url is not null && (url.StartsWith("View/", StringComparison.Ordinal) || url.StartsWith("Output/", StringComparison.Ordinal))).Take(16)];
                    if (candidates)
                    {
                        // Simple-mode candidates (F39); picking, downloading or exporting removes them from the cleanup registry.
                        CandidateCleanup.Register(session.User, result["images"] as JArray);
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.Warning($"EcomCraft background task failed: {ex.ReadableString()}");
                failure = Error(workflow ? "无法确认工作流执行结果。请在原生引擎核对队列和个人历史后再重试。"
                    : "无法确认引擎执行结果。请在原生引擎核对队列和个人历史后再重试。", "generation_uncertain");
            }
            finally
            {
                assignment.Dispose();
                task.Finish(failure, outputs);
                if (task.ClaimId > 0)
                {
                    TaskTiming.Forget(task.ClaimId);
                }
            }
        }, "ecomcraft background generate");
        for (int i = 0; i < 30; i++)
        {
            long fresh = session.Claims.Keys.FirstOrDefault(id => !claimsBefore.Contains(id));
            if (fresh != 0)
            {
                task.SetTaskId(fresh);
                // Tracked for the owner's queue estimate (F70) until the generation ends; an end seen just before this expires on its own.
                if (!task.Finished)
                {
                    TaskTiming.Watch(fresh, timing);
                }
                if (task.Finished && task.Failure is JObject early)
                {
                    return (JObject)early.DeepClone();
                }
                return new JObject() { ["queued"] = true, ["task_id"] = fresh };
            }
            if (task.Finished)
            {
                return FinishedResponse(task);
            }
            await Task.Delay(100);
        }
        if (task.Finished)
        {
            return FinishedResponse(task);
        }
        return new JObject() { ["queued"] = true, ["task_id"] = null };
    }

    /// <summary>The response for a task that finished during the submit call. A fully cached generation can complete before its claim is ever
    /// observed, so the claim ID never becomes known and the outcome is never recorded; such fast tasks return their images inline instead,
    /// the same way the cut-out-only branch does.</summary>
    private static JObject FinishedResponse(BackgroundTask task)
    {
        if (task.Failure is JObject failed)
        {
            return (JObject)failed.DeepClone();
        }
        return new JObject() { ["queued"] = true, ["task_id"] = null, ["images"] = new JArray(task.ImageUrls) };
    }
}
