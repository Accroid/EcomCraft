using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.WebAPI;

namespace EcomCraftStudio.Diagnostics;

/// <summary>Administrator-only, side-effect-free diagnostic API with an explicit response allowlist.</summary>
[API.APIClass("EcomCraft M0 configuration observations. No connection probes, generation, routing or automatic acceptance results.")]
public class DiagnosticsApi(PermInfo permission)
{
    /// <summary>Permission also checked on direct invocation, not merely at API registration.</summary>
    private readonly PermInfo RequiredPermission = permission;

    /// <summary>Returns a configuration snapshot, retaining all six live verification gates.</summary>
    [API.APIDescription("Read an administrator-only EcomCraft M0 snapshot from host memory. Does not contact backends. sampled_at is not a heartbeat; backend IDs are host references, not stable EcomCraft node IDs. Unknown capabilities and live evidence remain unverified.",
        """
            "schema_version": 1,
            "extension_version": "0.1.0",
            "scope": "configuration_snapshot_only",
            "sampled_at": "2026-10-17T00:00:00.0000000+00:00",
            "m0_status": "blocked", // or pending_verification; never ready
            "checks": [{"id":"authentication","status":"blocked","summary":"账号认证","detail":"..."}],
            "nodes": [{"configured_backend_id":0,"node_id":null,"name":"图片节点 A","backend_type":"swarmswarmbackend","kind":"remote_swarm","engine_reports_capacity":false,"allow_forwarding":false,"observation_failed":false,"last_successful_heartbeat_at":null,"last_error_summary":null,"backend_version":null,"approved_capabilities":[],"capability_status":"unverified","environment_status":"unverified","engine":{},"execution_backends":[]}],
            "spikes": [{"id":"SPIKE-01","title":"远程 Swarm 接入与双机执行","status":"pending_verification","evidence":"..."}]
        """)]
    public Task<JObject> EcomCraftGetDiagnostics(Session session)
    {
        if (session?.User is null || !session.User.HasPermission(RequiredPermission))
        {
            return Task.FromResult(new JObject() { ["error"] = "无权读取 EcomCraft 管理员诊断。", ["error_id"] = "bad_permissions" });
        }
        if (Program.Backends is null)
        {
            return Unavailable();
        }
        try
        {
            (HostObservation host, IReadOnlyList<NodeObservation> nodes) = SwarmSnapshotReader.Read();
            DiagnosticReport report = ReadinessEvaluator.Evaluate(host, nodes);
            return Task.FromResult(new JObject()
            {
                ["schema_version"] = 1,
                ["extension_version"] = EcomCraft.ExtensionVersion,
                ["scope"] = "configuration_snapshot_only",
                ["sampled_at"] = DateTimeOffset.UtcNow.ToString("O"),
                ["m0_status"] = report.M0Status,
                ["checks"] = new JArray(report.Checks.Select(c => new JObject()
                {
                    ["id"] = c.Id, ["status"] = c.Status, ["summary"] = c.Summary, ["detail"] = c.Detail
                })),
                ["nodes"] = new JArray(nodes.Select(NodeToJson)),
                ["spikes"] = new JArray(report.Spikes.Select(s => new JObject()
                {
                    ["id"] = s.Id, ["title"] = s.Title, ["status"] = s.Status, ["evidence"] = s.Evidence
                }))
            });
        }
        catch (Exception)
        {
            // Diagnostics must not copy raw host errors into an API response or browser log.
            return Unavailable();
        }
    }

    /// <summary>Projects only approved diagnostic fields, never serializing a host backend object.</summary>
    private static JObject NodeToJson(NodeObservation node)
    {
        return new()
        {
            ["configured_backend_id"] = node.BackendId,
            ["node_id"] = null,
            ["name"] = node.Name,
            ["backend_type"] = node.BackendType,
            ["kind"] = node.Kind,
            ["engine_reports_capacity"] = ReadinessEvaluator.EngineReportsCapacity(node),
            ["allow_forwarding"] = node.AllowForwarding,
            ["observation_failed"] = node.ObservationFailed,
            ["last_successful_heartbeat_at"] = null,
            ["last_error_summary"] = null,
            ["backend_version"] = null,
            ["approved_capabilities"] = new JArray(),
            ["capability_status"] = "unverified",
            ["environment_status"] = "unverified",
            ["engine"] = EngineToJson(node.Engine),
            ["execution_backends"] = new JArray(node.ExecutionBackends.Select(EngineToJson))
        };
    }

    /// <summary>Serializes the narrow engine signal without treating it as a successful health probe.</summary>
    private static JObject EngineToJson(ExecutionObservation engine)
    {
        return new()
        {
            ["host_backend_id"] = engine.BackendId,
            ["remote_backend_id"] = engine.RemoteBackendId,
            ["backend_type"] = engine.BackendType,
            ["status"] = engine.EngineStatus,
            ["enabled"] = engine.Enabled,
            ["shutdown_reserved"] = engine.ShutdownReserved,
            ["reservations"] = engine.Reservations,
            ["model_load_reserved"] = engine.ModelLoadReserved,
            ["usages"] = engine.Usages,
            ["max_usages"] = engine.MaxUsages,
            ["can_load_models"] = engine.CanLoadModels
        };
    }

    /// <summary>Fails closed if host initialization or snapshot collection is incomplete.</summary>
    private static Task<JObject> Unavailable()
    {
        return Task.FromResult(new JObject()
        {
            ["error"] = "宿主尚未就绪或诊断快照读取失败。请刷新；持续失败时由管理员检查宿主状态。",
            ["error_id"] = "ecomcraft_diagnostics_unavailable"
        });
    }
}
