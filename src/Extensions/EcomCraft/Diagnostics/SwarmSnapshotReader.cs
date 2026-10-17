using FreneticUtilities.FreneticExtensions;
using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;

namespace EcomCraftStudio.Diagnostics;

/// <summary>Projects existing in-memory host state onto a deliberately non-secret diagnostic model.</summary>
public static class SwarmSnapshotReader
{
    /// <summary>Reads one best-effort observation; does not probe connections or mutate the host.</summary>
    public static (HostObservation Host, IReadOnlyList<NodeObservation> Nodes) Read()
    {
        Settings settings = Program.ServerSettings;
        HostObservation host = new(settings.UserAuthorization.AuthorizationRequired, settings.UserAuthorization.AllowLocalhostBypass,
            settings.Paths.AppendUserNameToOutputPath, settings.Maintenance.AutoPullDevUpdates, settings.Network.PortCanChange);
        BackendHandler.BackendData[] all = Program.Backends.AllBackends.Values.ToArray();
        List<NodeObservation> nodes = [];
        foreach (BackendHandler.BackendData data in all.OrderBy(d => d.ID))
        {
            if (data.AbstractBackend is null || !data.AbstractBackend.IsReal)
            {
                continue;
            }
            try
            {
                AbstractBackend backend = data.AbstractBackend;
                SwarmSwarmBackend remote = backend as SwarmSwarmBackend;
                string type = data.BackType?.ID ?? "unknown";
                string kind = remote is not null ? "remote_swarm" : backend is ComfyUISelfStartBackend ? "local_comfy" : "unsupported";
                List<ExecutionObservation> executors = [];
                if (remote is not null)
                {
                    foreach (BackendHandler.BackendData child in all)
                    {
                        if (child.AbstractBackend is SwarmSwarmBackend childRemote && !childRemote.IsReal && ReferenceEquals(childRemote.Parent, remote))
                        {
                            executors.Add(ReadEngine(child, childRemote.LinkedRemoteBackendType ?? "unknown", childRemote.LinkedRemoteBackendID));
                        }
                    }
                }
                nodes.Add(new(data.ID, backend.Title ?? "", type, kind, ReadEngine(data, type), remote?.Settings?.AllowForwarding,
                    executors.OrderBy(e => e.BackendId).ToArray()));
            }
            catch (Exception)
            {
                // Never echo exception text: upstream failures can contain addresses or credentials.
                nodes.Add(new(data.ID, "观测失败", "unknown", "unsupported",
                    new(data.ID, null, "unknown", "unknown", false, true, 0, false, 0, 0, false), null, [], true));
            }
        }
        return (host, nodes);
    }

    /// <summary>Copies only execution flags and counts, never SettingsRaw or a backend address.</summary>
    private static ExecutionObservation ReadEngine(BackendHandler.BackendData data, string type, int? remoteBackendId = null)
    {
        AbstractBackend backend = data.AbstractBackend;
        BackendStatus observedStatus = backend.Status;
        string status = observedStatus switch
        {
            BackendStatus.DISABLED or BackendStatus.ERRORED or BackendStatus.WAITING or BackendStatus.LOADING or BackendStatus.IDLE or BackendStatus.RUNNING
                => observedStatus.ToString().ToLowerFast(),
            _ => "unknown"
        };
        return new(data.ID, remoteBackendId, type, status, backend.IsEnabled, backend.ShutDownReserve, backend.Reservations,
            data.ReserveModelLoad, data.Usages, backend.MaxUsages, backend.CanLoadModels);
    }
}
