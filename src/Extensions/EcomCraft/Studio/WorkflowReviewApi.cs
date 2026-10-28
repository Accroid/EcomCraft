using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Backends;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Workflow submission and review. Reviewers see parameter definitions and dependency problems, never the raw graph or server addresses.</summary>
[API.APIClass("EcomCraft workflow review: submit discovered ComfyUI workflows, review them and publish frozen snapshots with reviewer-configured parameters.")]
public static class WorkflowReviewApi
{
    /// <summary>The content a review applies to: the current source version, or the published snapshot when the source is unavailable or its newer version was rejected.</summary>
    private sealed record class ReviewVersion(string Canvas, JObject Prompt, JObject BaseParams, JObject BaseValues, string Description, string Fingerprint, string Server);

    /// <summary>Whether the session may review workflows.</summary>
    private static bool CanReview(Session session) => StudioApi.CanView(session) && session.User.HasPermission(EcomCraft.PermReviewWorkflows);

    /// <summary>Whether the session may submit workflows.</summary>
    private static bool CanSubmit(Session session) => StudioApi.CanView(session) && session.User.HasPermission(EcomCraft.PermSubmitWorkflows);

    /// <summary>The part of "Server/path" after the server.</summary>
    private static string ShortName(string id) => id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id;

    /// <summary>Trimmed text, or null when the token is not a string.</summary>
    private static string Text(JToken token) => token?.Type == JTokenType.String ? token.Value<string>().Trim() : null;

    /// <summary>Selects the version a review shows and publishes. Null when neither a valid source nor a snapshot exists.</summary>
    private static ReviewVersion VersionFor(WorkflowDiscovery.Entry entry, WorkflowReview.Record record)
    {
        WorkflowReview.Snapshot snapshot = record?.Snapshot;
        bool useSource = entry?.Workflow is not null && (snapshot is null || entry.Fingerprint == snapshot.Fingerprint
            || WorkflowReview.State(entry, record) == WorkflowReview.Rereview);
        if (useSource)
        {
            return new(entry.Workflow.Workflow, StudioApi.ParseWorkflowObject(entry.Workflow.Prompt), StudioApi.ParseWorkflowObject(entry.Workflow.CustomParams),
                StudioApi.ParseWorkflowObject(entry.Workflow.ParamValues) ?? new JObject(), entry.Workflow.Description, entry.Fingerprint, entry.Server);
        }
        if (snapshot is null)
        {
            return null;
        }
        return new(snapshot.Canvas, StudioApi.ParseWorkflowObject(snapshot.Prompt), StudioApi.ParseWorkflowObject(snapshot.BaseParams),
            StudioApi.ParseWorkflowObject(snapshot.BaseValues) ?? new JObject(), snapshot.Description, snapshot.Fingerprint, snapshot.Server);
    }

    /// <summary>Which execution servers can run a configured version, with up to three missing nodes or models each. Names only, no addresses.</summary>
    internal static JArray Runnability(JObject prompt, JObject metadata, JObject values)
    {
        JArray result = new();
        foreach (BackendHandler.T2IBackendData data in ExecutionServers.All)
        {
            JObject item = new() { ["name"] = ExecutionServers.DisplayName(data) };
            if (!ExecutionServers.IsReady(data))
            {
                item["ok"] = false;
                item["problems"] = new JArray("离线");
            }
            else
            {
                item["ok"] = NativeWorkflowParameters.Supports(prompt, metadata, values, ExecutionServers.Schema(data), out List<string> problems);
                item["problems"] = new JArray(problems.Take(3));
            }
            result.Add(item);
        }
        return result;
    }

    /// <summary>One row of the review list.</summary>
    private static JObject ReviewItem(string id, WorkflowDiscovery.Entry entry, WorkflowReview.Record record)
    {
        string state = WorkflowReview.State(entry, record);
        ReviewVersion version = state == WorkflowReview.Invalid ? null : VersionFor(entry, record);
        JArray runnable = new();
        if (version?.Prompt is not null && version.BaseParams is not null)
        {
            WorkflowReview.Apply(version.BaseParams, version.BaseValues, StudioApi.ParseWorkflowObject(record?.Snapshot?.Config), out JObject metadata, out JObject values);
            runnable = Runnability(version.Prompt, metadata, values);
        }
        return new JObject()
        {
            ["id"] = id,
            ["name"] = ShortName(id),
            ["server"] = entry?.Server ?? record?.Snapshot?.Server ?? "",
            ["source_online"] = WorkflowDiscovery.IsSourceOnline(entry),
            ["source_missing"] = entry is null,
            ["state"] = state,
            ["kind"] = version?.Prompt is null ? null : StudioApi.InferWorkflowKind(version.Prompt.ToString(Formatting.None)),
            ["problem"] = entry?.Problem,
            ["submitted_by"] = record?.SubmittedBy,
            ["submit_note"] = record?.SubmitNote,
            ["submitted_at"] = record?.SubmittedAt > 0 ? record.SubmittedAt : null,
            ["reject_reason"] = record?.RejectReason,
            ["reviewer"] = record?.Reviewer,
            ["reviewed_at"] = record?.ReviewedAt > 0 ? record.ReviewedAt : null,
            ["published_at"] = record?.Snapshot?.ReviewedAt,
            ["runnable"] = runnable
        };
    }

    [API.APIDescription("List discovered workflows and published snapshots with their review state and the execution servers that can run them. Requires the review permission.", "workflows: [{ id: \"B/工作流1\", state: \"pending\", runnable: [] }]")]
    public static Task<JObject> EcomCraftStudioReviewList(Session session)
    {
        if (!CanReview(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有审核工作流的权限。", "bad_permissions"));
        }
        WorkflowDiscovery.EnsureFresh();
        Dictionary<string, WorkflowReview.Record> records = WorkflowReview.All();
        JArray items = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowDiscovery.Entry entry in WorkflowDiscovery.ListEntries())
        {
            seen.Add(entry.Id);
            items.Add(ReviewItem(entry.Id, entry, records.GetValueOrDefault(entry.Id)));
        }
        foreach (KeyValuePair<string, WorkflowReview.Record> pair in records.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            // A published snapshot outlives its source; other records without a source have nothing left to review.
            if (!seen.Contains(pair.Key) && pair.Value.Snapshot is not null && pair.Value.Status != WorkflowReview.Own)
            {
                items.Add(ReviewItem(pair.Key, null, pair.Value));
            }
        }
        return Task.FromResult(new JObject() { ["workflows"] = items });
    }

    [API.APIDescription("Read the parameters of one workflow for review, with the current reviewer configuration and runnable servers. Requires the review permission.", "id: \"B/工作流1\", parameters: [], runnable: []")]
    public static Task<JObject> EcomCraftStudioReviewDetail(Session session, string workflow_id)
    {
        if (!CanReview(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有审核工作流的权限。", "bad_permissions"));
        }
        WorkflowDiscovery.Entry entry = WorkflowDiscovery.Find(workflow_id);
        WorkflowReview.Record record = WorkflowReview.Get(workflow_id);
        // Own uploads are private to their owner (F60) until submitted through a later review step.
        if (record?.Status == WorkflowReview.Own)
        {
            return Task.FromResult(StudioApi.Error("这是账号自用的工作流，不在审核范围内。", "invalid_workflow"));
        }
        ReviewVersion version = VersionFor(entry, record);
        if (version?.Prompt is null || version.BaseParams is null)
        {
            return Task.FromResult(StudioApi.Error(entry?.Problem ?? "工作流不存在、来源服务器离线或无法通过校验。请刷新列表。", "invalid_workflow"));
        }
        WorkflowReview.Apply(version.BaseParams, version.BaseValues, StudioApi.ParseWorkflowObject(record?.Snapshot?.Config), out JObject metadata, out JObject values);
        JArray parameters = new();
        foreach (JProperty property in version.BaseParams.Properties())
        {
            if (property.Value is not JObject original || metadata[property.Name] is not JObject definition)
            {
                continue;
            }
            parameters.Add(new JObject()
            {
                ["id"] = property.Name,
                ["name"] = definition["name"]?.ToString() ?? property.Name,
                ["original_name"] = original["name"]?.ToString() ?? property.Name,
                ["type"] = definition["type"]?.ToString(),
                ["form"] = definition["form"]?.DeepClone(),
                ["locked"] = WorkflowReview.IsLocked(definition),
                ["visible"] = definition.Value<bool?>("visible") != false,
                ["default"] = values[property.Name]?.DeepClone() ?? definition["default"]?.DeepClone(),
                ["original_default"] = version.BaseValues[property.Name]?.DeepClone() ?? original["default"]?.DeepClone(),
                ["min"] = definition["min"]?.DeepClone(),
                ["max"] = definition["max"]?.DeepClone(),
                ["original_min"] = original["min"]?.DeepClone(),
                ["original_max"] = original["max"]?.DeepClone(),
                ["step"] = definition["step"]?.DeepClone(),
                ["values"] = definition["values"]?.DeepClone(),
                ["all_values"] = original["values"]?.DeepClone(),
                ["description"] = definition["description"]?.ToString() ?? ""
            });
        }
        string state = WorkflowReview.State(entry, record);
        return Task.FromResult(new JObject()
        {
            ["id"] = workflow_id,
            ["name"] = ShortName(workflow_id),
            ["server"] = version.Server ?? "",
            ["state"] = state,
            ["source_online"] = WorkflowDiscovery.IsSourceOnline(entry),
            ["reviewing_source"] = version.Fingerprint == entry?.Fingerprint,
            ["kind"] = StudioApi.InferWorkflowKind(version.Prompt.ToString(Formatting.None)),
            ["description"] = record?.Snapshot?.Description ?? version.Description ?? "",
            ["submitted_by"] = record?.SubmittedBy,
            ["submit_note"] = record?.SubmitNote,
            ["reject_reason"] = record?.RejectReason,
            ["parameters"] = parameters,
            ["runnable"] = Runnability(version.Prompt, metadata, values)
        });
    }

    /// <summary>Validates a reviewer configuration against the discovered bindings, then every resulting default that users cannot change.</summary>
    private static string ValidateConfig(Session session, ReviewVersion version, JObject config)
    {
        if (config.Count > 60)
        {
            return "参数配置数量超过限制。";
        }
        foreach (JProperty property in config.Properties())
        {
            if (version.BaseParams[property.Name] is not JObject original || property.Value is not JObject setting)
            {
                return $"参数“{property.Name}”不属于此工作流。";
            }
            string label = original["name"]?.ToString() ?? property.Name;
            string type = original["type"]?.ToString();
            if (setting["name"] is not null && Text(setting["name"]) is not { Length: > 0 and <= WorkflowReview.MaxNameLength })
            {
                return $"参数“{label}”的显示名须为 1–{WorkflowReview.MaxNameLength} 个字符。";
            }
            if (setting["visible"] is not null && setting["visible"].Type != JTokenType.Boolean)
            {
                return $"参数“{label}”的显示设置无效。";
            }
            if (setting.Value<bool?>("visible") == false && WorkflowReview.IsLocked(original))
            {
                return $"参数“{label}”是页面固定显示的项，不能隐藏。";
            }
            if ((setting["min"] is not null || setting["max"] is not null)
                && (type is not ("integer" or "decimal") || !WorkflowReview.TryRange(original, setting, out _, out _)))
            {
                return $"参数“{label}”的取值范围无效：最小值不能大于最大值，并且须在工作流原有范围内。";
            }
            if (setting["values"] is not null)
            {
                HashSet<string> allowed = new((original["values"] as JArray ?? new JArray()).Select(value => value.ToString()), StringComparer.Ordinal);
                if (type != "dropdown" || setting["values"] is not JArray chosen || chosen.Count == 0
                    || chosen.Any(value => value.Type != JTokenType.String || !allowed.Contains(value.ToString())))
                {
                    return $"参数“{label}”的可选项必须是工作流原有选项中的至少一项。";
                }
            }
            if (setting["default"] is not null && setting["default"] is not JValue)
            {
                return $"参数“{label}”的默认值格式无效。";
            }
        }
        WorkflowReview.Apply(version.BaseParams, version.BaseValues, config, out JObject metadata, out JObject values);
        foreach (JProperty property in metadata.Properties())
        {
            JObject definition = (JObject)property.Value;
            // Visible page fields take the user's value on every run; only fixed and workflow-form values must already be valid.
            if (!string.IsNullOrEmpty(definition["form"]?.ToString()) && definition.Value<bool?>("visible") != false)
            {
                continue;
            }
            JToken value = values[property.Name] ?? definition["default"];
            string error = null;
            if (value is null || value.Type == JTokenType.Null || !StudioApi.ValidatePublishedWorkflowInput(session, property.Name, value, definition, out error))
            {
                string label = definition["name"]?.ToString() ?? property.Name;
                return $"参数“{label}”的默认值无效：{error ?? "缺少默认值。"}";
            }
        }
        return null;
    }

    [API.APIDescription("Approve (publish a frozen snapshot with the given parameter configuration), reject with a reason, or unpublish a workflow. Requires the review permission. rawInput is the root request JSON.", "success: true, state: \"published\"")]
    public static Task<JObject> EcomCraftStudioReviewDecide(Session session, JObject rawInput)
    {
        if (!CanReview(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有审核工作流的权限。", "bad_permissions"));
        }
        string id = Text(rawInput["workflow_id"]);
        string action = Text(rawInput["action"]);
        if (string.IsNullOrEmpty(id) || action is not ("approve" or "reject" or "unpublish"))
        {
            return Task.FromResult(StudioApi.Error("未知的审核操作。", "invalid_review"));
        }
        if (WorkflowReview.OwnerOf(id) is not null)
        {
            return Task.FromResult(StudioApi.Error("这是账号自用的工作流，不在审核范围内。", "invalid_workflow"));
        }
        WorkflowDiscovery.Entry entry = WorkflowDiscovery.Find(id);
        string reviewer = session.User.UserID;
        string error;
        if (action == "reject")
        {
            string reason = Text(rawInput["reason"]);
            if (string.IsNullOrEmpty(reason) || reason.Length > WorkflowReview.MaxTextLength)
            {
                return Task.FromResult(StudioApi.Error($"驳回时必须填写原因（最多 {WorkflowReview.MaxTextLength} 个字符）。", "invalid_review"));
            }
            if (entry is null)
            {
                return Task.FromResult(StudioApi.Error("工作流来源已不存在，无需驳回。请刷新列表。", "invalid_workflow"));
            }
            error = WorkflowReview.Update(id, record =>
            {
                if (WorkflowReview.State(entry, record) is not (WorkflowReview.Pending or WorkflowReview.Rereview))
                {
                    return "只有待审核或待重新审核的工作流可以驳回。请刷新列表。";
                }
                record.RejectedFingerprint = entry.Fingerprint;
                record.RejectReason = reason;
                record.Reviewer = reviewer;
                record.ReviewedAt = WorkflowReview.Now;
                if (record.Snapshot is null)
                {
                    record.Status = WorkflowReview.Rejected;
                }
                return null;
            });
        }
        else if (action == "unpublish")
        {
            error = WorkflowReview.Update(id, record =>
            {
                if (record.Snapshot is null)
                {
                    return "该工作流当前没有发布。请刷新列表。";
                }
                record.Snapshot = null;
                record.Status = WorkflowReview.Unpublished;
                record.Reviewer = reviewer;
                record.ReviewedAt = WorkflowReview.Now;
                return null;
            });
        }
        else
        {
            WorkflowReview.Record current = WorkflowReview.Get(id);
            ReviewVersion version = VersionFor(entry, current);
            if (version?.Prompt is null || version.BaseParams is null)
            {
                return Task.FromResult(StudioApi.Error(entry?.Problem ?? "工作流来源不可用或无法通过校验，不能发布。请刷新列表。", "invalid_workflow"));
            }
            JToken rawConfig = rawInput["parameters"];
            if (rawConfig is not null && rawConfig.Type is not (JTokenType.Object or JTokenType.Null))
            {
                return Task.FromResult(StudioApi.Error("参数配置格式无效。", "invalid_review"));
            }
            JObject config = rawConfig as JObject ?? new JObject();
            string description = Text(rawInput["description"]);
            if (description?.Length > WorkflowReview.MaxTextLength)
            {
                return Task.FromResult(StudioApi.Error($"说明最多 {WorkflowReview.MaxTextLength} 个字符。", "invalid_review"));
            }
            string configError = ValidateConfig(session, version, config);
            if (configError is not null)
            {
                return Task.FromResult(StudioApi.Error(configError, "invalid_review"));
            }
            WorkflowReview.Snapshot snapshot = new()
            {
                Canvas = version.Canvas,
                Prompt = version.Prompt.ToString(Formatting.None),
                BaseParams = version.BaseParams.ToString(Formatting.None),
                BaseValues = version.BaseValues.ToString(Formatting.None),
                Config = config.ToString(Formatting.None),
                Description = string.IsNullOrEmpty(description) ? version.Description : description,
                Fingerprint = version.Fingerprint,
                Server = version.Server,
                Reviewer = reviewer,
                ReviewedAt = WorkflowReview.Now
            };
            error = WorkflowReview.Update(id, record =>
            {
                // The version was chosen outside the lock; refuse when another reviewer changed the record meanwhile.
                if (record.Snapshot?.Fingerprint != current?.Snapshot?.Fingerprint || record.RejectedFingerprint != current?.RejectedFingerprint)
                {
                    return "该工作流的审核状态刚被其他人修改，请刷新后重新审核。";
                }
                record.Snapshot = snapshot;
                record.Status = WorkflowReview.Published;
                record.RejectedFingerprint = null;
                record.RejectReason = null;
                record.Reviewer = reviewer;
                record.ReviewedAt = snapshot.ReviewedAt;
                return null;
            });
        }
        if (error is not null)
        {
            return Task.FromResult(StudioApi.Error(error, error == WorkflowReview.SaveFailedMessage ? "review_save_failed" : "workflow_state"));
        }
        return Task.FromResult(new JObject() { ["success"] = true, ["state"] = WorkflowReview.State(entry, WorkflowReview.Get(id)) });
    }

    [API.APIDescription("List discovered workflows that can be submitted for review, with the state of each and the caller's own submission notes and rejection reasons.", "workflows: [{ id: \"B/工作流1\", state: \"pending\", mine: false }]")]
    public static Task<JObject> EcomCraftStudioWorkflowSubmissions(Session session)
    {
        if (!CanSubmit(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有提交工作流审核的权限。", "bad_permissions"));
        }
        WorkflowDiscovery.EnsureFresh();
        Dictionary<string, WorkflowReview.Record> records = WorkflowReview.All();
        string user = session.User.UserID;
        JArray items = new();
        foreach (WorkflowDiscovery.Entry entry in WorkflowDiscovery.ListEntries())
        {
            WorkflowReview.Record record = records.GetValueOrDefault(entry.Id);
            string state = WorkflowReview.State(entry, record);
            bool mine = record?.SubmittedBy == user;
            bool newerRejected = record?.Snapshot is not null && entry.Problem is null && entry.Fingerprint == record.RejectedFingerprint;
            items.Add(new JObject()
            {
                ["id"] = entry.Id,
                ["name"] = ShortName(entry.Id),
                ["server"] = entry.Server,
                ["source_online"] = WorkflowDiscovery.IsSourceOnline(entry),
                ["state"] = state,
                ["problem"] = entry.Problem,
                ["mine"] = mine,
                ["submit_note"] = mine ? record.SubmitNote : null,
                ["submitted_at"] = mine && record.SubmittedAt > 0 ? record.SubmittedAt : null,
                // Rejection reasons go to the submitter only.
                ["reject_reason"] = mine && (state == WorkflowReview.Rejected || newerRejected) ? record.RejectReason : null,
                ["can_submit"] = state is WorkflowReview.Pending or WorkflowReview.Rejected or WorkflowReview.Unpublished or WorkflowReview.Rereview
                    || (state == WorkflowReview.Published && newerRejected)
            });
        }
        return Task.FromResult(new JObject() { ["workflows"] = items });
    }

    [API.APIDescription("Submit a discovered workflow for review with an optional note. Resubmitting a rejected workflow returns it to review.", "success: true, state: \"pending\"")]
    public static Task<JObject> EcomCraftStudioSubmitWorkflow(Session session, string workflow_id, string note = "")
    {
        if (!CanSubmit(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有提交工作流审核的权限。", "bad_permissions"));
        }
        WorkflowDiscovery.Entry entry = WorkflowDiscovery.Find(workflow_id);
        if (entry is null)
        {
            return Task.FromResult(StudioApi.Error("工作流不存在或来源服务器离线。请在 ComfyUI 中保存后刷新列表。", "invalid_workflow"));
        }
        if (entry.Problem is not null)
        {
            return Task.FromResult(StudioApi.Error($"{entry.Problem}请在 ComfyUI 中修改后重新保存。", "invalid_workflow"));
        }
        note = (note ?? "").Trim();
        if (note.Length > WorkflowReview.MaxTextLength)
        {
            return Task.FromResult(StudioApi.Error($"说明最多 {WorkflowReview.MaxTextLength} 个字符。", "invalid_review"));
        }
        string user = session.User.UserID;
        string error = WorkflowReview.Update(entry.Id, record =>
        {
            string state = WorkflowReview.State(entry, record);
            if (state == WorkflowReview.Published)
            {
                if (record.Snapshot is null || entry.Fingerprint == record.Snapshot.Fingerprint)
                {
                    return "该工作流的当前版本已经发布，无需再次提交。";
                }
                // The newer version was rejected; resubmitting puts it back in review while users keep the snapshot.
                record.RejectedFingerprint = null;
            }
            else if (state == WorkflowReview.Rejected || state == WorkflowReview.Unpublished)
            {
                record.RejectedFingerprint = null;
                record.Status = null;
            }
            record.SubmittedBy = user;
            record.SubmitNote = note;
            record.SubmittedAt = WorkflowReview.Now;
            return null;
        });
        if (error is not null)
        {
            return Task.FromResult(StudioApi.Error(error, error == WorkflowReview.SaveFailedMessage ? "review_save_failed" : "workflow_state"));
        }
        return Task.FromResult(new JObject() { ["success"] = true, ["state"] = WorkflowReview.State(entry, WorkflowReview.Get(entry.Id)) });
    }
}
