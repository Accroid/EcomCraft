namespace EcomCraftStudio.Diagnostics;

/// <summary>Pure diagnostic rules. Never routes work, connects to nodes or writes settings.</summary>
public static class ReadinessEvaluator
{
    /// <summary>Evaluates only observable configuration, retaining all live-verification gates.</summary>
    public static DiagnosticReport Evaluate(HostObservation host, IReadOnlyList<NodeObservation> nodes)
    {
        List<DiagnosticCheck> checks =
        [
            Configuration("authentication", host.AuthenticationRequired, "账号认证", "必须开启宿主 AuthorizationRequired；内网不是认证边界。"),
            Configuration("localhost_bypass", !host.LocalhostBypass, "本机登录绕过", "共享部署需关闭 AllowLocalhostBypass；反向代理与真实账号还需实测。"),
            Configuration("private_output", host.UserOutputIsolation, "按用户隔离输出", "必须开启 AppendUserNameToOutputPath；私有文件 URL 越权仍需负向测试。"),
            Configuration("controlled_updates", !host.AutomaticUpdates, "受控更新", "必须关闭 AutoPullDevUpdates；关闭此项不等于已建立审批和回滚流程。"),
            new("fixed_port", host.PortCanChange ? "warning" : "passed", "统一入口端口", host.PortCanChange
                ? "宿主允许端口占用时自动换端口；请固定端口或验证反向代理配置。" : "宿主不会自动换端口；统一 URL 和节点故障场景仍需实测。"),
            new("configured_nodes", nodes.Count == 0 ? "blocked" : "passed", "后端配置", $"读取到 {nodes.Count} 个宿主后端配置；配置引用不代表稳定业务节点 ID。"),
            new("remote_node_count", "pending_verification", "独立远程节点", $"读取到 {nodes.Count(n => n.Kind == "remote_swarm")} 个远程 Swarm 配置；需至少两台独立 PC 实测，不能按配置数判通过。")
        ];
        foreach (NodeObservation node in nodes)
        {
            if (node.ObservationFailed)
            {
                checks.Add(new($"node_{node.BackendId}_observation", "blocked", "节点观测失败", $"后端配置 {node.BackendId} 未能完成安全快照；请刷新并由管理员检查宿主状态。"));
                continue;
            }
            if (node.Kind == "remote_swarm")
            {
                string status = node.AllowForwarding == false ? "passed" : node.Engine.Enabled ? "blocked" : "warning";
                checks.Add(new($"node_{node.BackendId}_forwarding", status, "远程继续转发", node.AllowForwarding == false
                    ? $"后端配置 {node.BackendId} 已关闭 AllowForwarding；仍需核对远程直接后端的实际去向和出网规则。"
                    : $"后端配置 {node.BackendId} 的 AllowForwarding 开启或未知；启用前必须关闭并验证完整执行路径。"));
            }
            if (node.Kind == "unsupported")
            {
                checks.Add(new($"node_{node.BackendId}_type", "pending_verification", "尚未验证的后端类型", $"后端配置 {node.BackendId} 不在本切片可解释的本机 Comfy / 远程 Swarm 范围，不推断可用性或本地属性。"));
            }
        }
        SpikeCheck[] spikes =
        [
            new("SPIKE-01", "远程 Swarm 接入与双机执行", "pending_verification", "需两台独立生成 PC 的连接、重复生成及实际执行端证据。"),
            new("SPIKE-02", "逐任务策略与禁止隐式出云", "pending_verification", "本扩展不接管路由；需服务端强制策略及停掉全部本地节点后的网络负向测试。"),
            new("SPIKE-03", "节点健康、维护与能力匹配", "pending_verification", "引擎内存状态不是心跳；需离线、维护、未知状态和工作流不匹配实测。"),
            new("SPIKE-04", "结果回传与成功归档边界", "pending_verification", "结果 URL 不等于文件写入成功；需断网、大文件、磁盘不足和归档中断实测。"),
            new("SPIKE-05", "公共发布与私有素材访问控制", "pending_verification", "本扩展不实现素材库；需两个员工账号执行直接 URL / ID / API 越权及显式发布测试。"),
            new("SPIKE-06", "上游集成、审批与回滚", "pending_verification", "需独立集成分支、测试环境、批准记录和不丢业务数据的回滚演练。")
        ];
        return new(checks.Any(c => c.Status == "blocked") ? "blocked" : "pending_verification", checks, spikes);
    }

    /// <summary>Reports a narrow engine capacity signal, never permission to schedule or send data.</summary>
    public static bool EngineReportsCapacity(NodeObservation node)
    {
        if (node.ObservationFailed || !ReportsRunning(node.Engine))
        {
            return false;
        }
        if (node.Kind == "local_comfy")
        {
            return ExecutorHasCapacity(node.Engine);
        }
        if (node.Kind == "remote_swarm" && node.AllowForwarding == false)
        {
            return node.ExecutionBackends.Any(ExecutorHasCapacity);
        }
        return false;
    }

    /// <summary>Accepts only understood direct Comfy executors, never chained Swarm or unknown engines.</summary>
    private static bool ExecutorHasCapacity(ExecutionObservation engine)
    {
        return ReportsRunning(engine) && engine.CanLoadModels && engine.Usages >= 0 && engine.Usages < engine.MaxUsages
            && (engine.BackendType == "comfyui_selfstart" || engine.BackendType == "comfyui_api");
    }

    /// <summary>Unknown, disabled, loading, reserved or shutting-down engines fail closed.</summary>
    private static bool ReportsRunning(ExecutionObservation engine)
    {
        return engine.Enabled && engine.EngineStatus == "running" && !engine.ShutdownReserved && engine.Reservations == 0
            && !engine.ModelLoadReserved && engine.MaxUsages > 0;
    }

    /// <summary>Builds a configuration-only pass or blocker with its explicit verification boundary.</summary>
    private static DiagnosticCheck Configuration(string id, bool passed, string summary, string detail)
    {
        return new(id, passed ? "passed" : "blocked", summary, detail);
    }
}
