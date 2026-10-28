using System.Security.Cryptography;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Studio;

/// <summary>Own workflows (F60, decision U6): an account uploads a saved ComfyUI workflow and runs it at once, visible to nobody else.
/// The upload is the only place a client sends ComfyUI JSON; it is converted and checked like a discovered workflow, refused when it uses
/// code-running nodes or path inputs, and kept on the server. Clients get names, states and missing dependencies, never the graph.</summary>
[API.APIClass("EcomCraft own workflows: upload a saved ComfyUI workflow for one's own use, list and delete them.")]
public static class OwnWorkflowApi
{
    /// <summary>Whether the account may upload and run own workflows.</summary>
    private static bool CanUse(Session session) => StudioApi.CanView(session) && session.User.HasPermission(Permissions.BasicImageGeneration)
        && session.User.HasPermission(EcomCraft.PermUsePublishedWorkflows);

    /// <summary>The list entry of one own workflow: name, kind, upload time and which execution servers can run it (names and missing dependencies only).</summary>
    private static JObject Item(string id, WorkflowReview.Record record)
    {
        WorkflowReview.Snapshot snapshot = record.Snapshot;
        JObject prompt = StudioApi.ParseWorkflowObject(snapshot?.Prompt);
        JArray runnable = new();
        if (prompt is not null)
        {
            WorkflowReview.Apply(StudioApi.ParseWorkflowObject(snapshot.BaseParams) ?? new JObject(), StudioApi.ParseWorkflowObject(snapshot.BaseValues) ?? new JObject(),
                null, out JObject metadata, out JObject values);
            runnable = WorkflowReviewApi.Runnability(prompt, metadata, values);
        }
        return new JObject()
        {
            ["id"] = id,
            ["name"] = record.Name ?? id,
            ["kind"] = prompt is null ? null : StudioApi.InferWorkflowKind(snapshot.Prompt),
            ["uploaded_at"] = record.UploadedAt,
            ["runnable"] = runnable,
            // Review of the submission for everyone (T73); the rejection reason goes to the owner only.
            ["review_state"] = WorkflowReview.OwnState(record),
            ["reject_reason"] = WorkflowReview.OwnState(record) == WorkflowReview.Rejected ? record.RejectReason : null,
            ["published_name"] = record.PublishedAs
        };
    }

    [API.APIDescription("Upload a ComfyUI workflow saved with ComfyUI's \"Save\" (not the API export) for this account's own use (F60). "
        + "It is checked like a discovered workflow and refused when it uses nodes that can run code or inputs that look like file paths; at most 20 per account, 2 MB each. "
        + "Returns the new workflow ID and which execution servers can run it.", "id: \"own-1a2b3c4d\", name: \"\", runnable: []")]
    public static Task<JObject> EcomCraftStudioWorkflowUpload(Session session, JObject rawInput)
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有上传工作流的权限。", "bad_permissions"));
        }
        string name = OwnWorkflowRules.CleanName(rawInput?["name"]?.ToString());
        string json = rawInput?["workflow"]?.Type == JTokenType.String ? rawInput["workflow"].ToString() : "";
        if (name.Length == 0)
        {
            return Task.FromResult(StudioApi.Error("请给工作流起一个名字。", "invalid_workflow"));
        }
        if (json.Length == 0 || json.Length > OwnWorkflowRules.MaxJsonLength)
        {
            return Task.FromResult(StudioApi.Error("工作流文件为空或超过 2 MB。", "workflow_too_large"));
        }
        JObject canvas = StudioApi.ParseWorkflowObject(json);
        if (canvas is null)
        {
            return Task.FromResult(StudioApi.Error("文件不是有效的 ComfyUI 工作流 JSON。", "invalid_workflow"));
        }
        if (OwnWorkflowRules.IsApiFormat(canvas))
        {
            return Task.FromResult(StudioApi.Error("这是 ComfyUI 的“导出（API）”格式。请在 ComfyUI 中用菜单“保存”（Save）得到的工作流文件上传。", "workflow_api_format"));
        }
        JObject prompt = NativeWorkflowConverter.Convert(canvas, out JObject metadata, out JObject values);
        if (prompt is null)
        {
            return Task.FromResult(StudioApi.Error("工作流结构暂不支持：需要正负提示词各接一个文本编码节点、子图连接完整，且可配置参数不超过 60 个。", "workflow_unsupported"));
        }
        List<OwnWorkflowRules.Problem> problems = OwnWorkflowRules.Problems(prompt, Program.ServerSettings.EcomCraft.DeniedWorkflowNodes);
        if (problems.Count > 0)
        {
            JObject refused = StudioApi.Error("工作流里有不允许使用的节点或参数，没有保存。", "workflow_denied");
            refused["problems"] = new JArray(problems.Take(10).Select(problem => new JObject()
            {
                ["code"] = problem.Code,
                ["node"] = problem.Node,
                ["input"] = problem.Input
            }));
            return Task.FromResult(refused);
        }
        string fingerprint = WorkflowCatalogFilter.Fingerprint(new JObject { ["prompt"] = prompt, ["parameters"] = metadata, ["values"] = values });
        NativeWorkflowParameters.AddChoices(metadata, ExecutionServers.All.Select(ExecutionServers.Schema).Where(schema => schema is not null).ToList());
        WorkflowReview.Record record = new()
        {
            Owner = session.User.UserID,
            Name = name,
            UploadedAt = WorkflowReview.Now,
            Snapshot = new WorkflowReview.Snapshot()
            {
                Canvas = json,
                Prompt = prompt.ToString(Formatting.None),
                BaseParams = metadata.ToString(Formatting.None),
                BaseValues = values.ToString(Formatting.None),
                Description = "账号上传的自用工作流。",
                Fingerprint = fingerprint,
                Server = "",
                ReviewedAt = WorkflowReview.Now
            }
        };
        // A random ID: it reveals nothing about the account or the name and survives SwarmUI's file name cleaning unchanged.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string id = $"own-{System.Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerFast()}";
            string error = WorkflowReview.AddOwn(id, record, OwnWorkflowRules.MaxPerAccount);
            if (error == "taken")
            {
                continue;
            }
            if (error == "limit")
            {
                return Task.FromResult(StudioApi.Error($"每个账号最多保存 {OwnWorkflowRules.MaxPerAccount} 个自用工作流，请先删除不用的。", "workflow_limit"));
            }
            if (error is not null)
            {
                return Task.FromResult(StudioApi.Error(error, "review_save_failed"));
            }
            return Task.FromResult(Item(id, WorkflowReview.Get(id) ?? record));
        }
        return Task.FromResult(StudioApi.Error("没有生成可用的工作流编号，请重试。", "review_save_failed"));
    }

    [API.APIDescription("List this account's own uploaded workflows (F60), newest first, with which execution servers can run them. Never returns the graph.",
        "workflows: [{ id: \"own-1a2b3c4d\", name: \"\", kind: \"image\", uploaded_at: 0, runnable: [] }], limit: 20")]
    public static Task<JObject> EcomCraftStudioMyWorkflows(Session session)
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有上传工作流的权限。", "bad_permissions"));
        }
        WorkflowReview.Restore();
        JArray items = new(WorkflowReview.OwnBy(session.User.UserID).Select(pair => Item(pair.Key, pair.Value)));
        return Task.FromResult(new JObject() { ["workflows"] = items, ["limit"] = OwnWorkflowRules.MaxPerAccount });
    }

    [API.APIDescription("Submit one of this account's own workflows for review (T73), so a reviewer can publish a copy for every account. "
        + "Allowed while it was never submitted or after a rejection; note is optional (up to 500 characters).", "success: true, review_state: \"pending\"")]
    public static Task<JObject> EcomCraftStudioWorkflowSubmitOwn(Session session, string workflow_id, string note = "")
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有上传工作流的权限。", "bad_permissions"));
        }
        note = (note ?? "").Trim();
        if (note.Length > WorkflowReview.MaxTextLength)
        {
            return Task.FromResult(StudioApi.Error($"提交说明最多 {WorkflowReview.MaxTextLength} 个字符。", "invalid_review"));
        }
        string userId = session.User.UserID;
        if (WorkflowReview.OwnerOf(workflow_id) != userId)
        {
            return Task.FromResult(StudioApi.Error("这个工作流不存在或不是你上传的，请刷新列表。", "invalid_workflow"));
        }
        string error = WorkflowReview.Update(workflow_id, record =>
        {
            if (record.Status != WorkflowReview.Own || record.Owner != userId)
            {
                return "这个工作流不存在或不是你上传的，请刷新列表。";
            }
            string state = WorkflowReview.OwnState(record);
            if (state == WorkflowReview.Pending)
            {
                return "已经提交过，正在等待审核。";
            }
            if (state == WorkflowReview.Published)
            {
                return "已经审核发布给所有账号，不需要再次提交。";
            }
            record.SubmittedBy = userId;
            record.SubmitNote = note;
            record.SubmittedAt = WorkflowReview.Now;
            record.RejectReason = null;
            return null;
        });
        if (error is not null)
        {
            return Task.FromResult(StudioApi.Error(error, error == WorkflowReview.SaveFailedMessage ? "review_save_failed" : "workflow_state"));
        }
        return Task.FromResult(new JObject() { ["success"] = true, ["review_state"] = WorkflowReview.Pending });
    }

    [API.APIDescription("Delete one of this account's own uploaded workflows (F60).", "success: true")]
    public static Task<JObject> EcomCraftStudioWorkflowDelete(Session session, string workflow_id)
    {
        if (!CanUse(session))
        {
            return Task.FromResult(StudioApi.Error("此账号没有上传工作流的权限。", "bad_permissions"));
        }
        string error = WorkflowReview.DeleteOwn(workflow_id, session.User.UserID);
        if (error == "not_found")
        {
            return Task.FromResult(StudioApi.Error("这个工作流不存在或不是你上传的，请刷新列表。", "invalid_workflow"));
        }
        if (error is not null)
        {
            return Task.FromResult(StudioApi.Error(error, "review_save_failed"));
        }
        return Task.FromResult(new JObject() { ["success"] = true });
    }
}
