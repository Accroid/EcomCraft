using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace EcomCraftStudio.Studio;

/// <summary>Resolves where the ComfyUI editor of a published workflow's source server is (administrators, F62/A13: the native
/// engine is admin-only). Local self-starting servers go through SwarmUI's ComfyUI proxy so no address is exposed and the
/// session cookie authorizes the page; registered LAN servers use their own address, which the administrator registered.</summary>
public static class WorkflowEditLink
{
    /// <summary>Everything the caller needs to open the editor or explain why it cannot be opened.</summary>
    /// <param name="Url">The editor page: a same-origin proxy path for local servers, or an absolute address for LAN servers.</param>
    /// <param name="ServerName">Display name of the source execution server; never an address.</param>
    /// <param name="WorkflowName">The workflow's own name (path after the server prefix) to open inside the editor.</param>
    /// <param name="Error">User-facing reason the editor cannot be opened; null when it can.</param>
    public sealed record class Result(string Url, string ServerName, string WorkflowName, string Error);

    /// <summary>The part of a "Server/path" workflow ID after the server.</summary>
    private static string ShortName(string id) => id.Contains('/') ? id[(id.IndexOf('/') + 1)..] : id;

    /// <summary>Resolves the editor page of the source server of a published native workflow. <paramref name="requesterLocal"/> picks
    /// the direct local address over the proxy when the administrator's browser runs on this server machine.</summary>
    public static Result Resolve(string workflowId, bool requesterLocal)
    {
        workflowId = workflowId?.Trim() ?? "";
        if (workflowId.Length == 0 || workflowId.Contains("..") || !WorkflowReview.NotOthersOwn(workflowId, null))
        {
            return new(null, null, null, "账号上传的工作流没有服务器来源；如需修改，请下载后重新上传新版本。");
        }
        if (!WorkflowReview.IsPublished(workflowId))
        {
            return new(null, null, null, "该工作流还没有已发布的版本。");
        }
        WorkflowDiscovery.Entry entry = WorkflowDiscovery.Find(workflowId);
        if (entry is null)
        {
            return new(null, null, null, "找不到该工作流的来源（系统生成的工作流不能在线编辑）。");
        }
        if (entry.Problem is not null)
        {
            return new(null, entry.Server, ShortName(workflowId), $"来源工作流目前无法通过校验：{entry.Problem}");
        }
        if (!WorkflowDiscovery.IsSourceOnline(entry))
        {
            return new(null, entry.Server, ShortName(workflowId), "来源执行服务器当前离线，稍后再试。");
        }
        BackendHandler.T2IBackendData server = ExecutionServers.All.FirstOrDefault(data => data.ID == entry.ServerId);
        if (server?.Backend is not ComfyUIAPIAbstractBackend backend)
        {
            return new(null, entry.Server, ShortName(workflowId), "该工作流不属于任何已登记的执行服务器，不能在线编辑。");
        }
        // The ComfyUI editor restores the last open workflow itself; local servers go through the session-checked proxy
        // (same origin, no address in the link) unless the administrator's browser is on this machine.
        string url = entry.Remote ? backend.WebAddress : (requesterLocal ? backend.WebAddress : "/ComfyBackendDirect/");
        return new(url, ExecutionServers.DisplayName(server), ShortName(workflowId), null);
    }
}
