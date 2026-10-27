using SwarmUI.Backends;
using SwarmUI.Builtin_ComfyUIBackend;

namespace EcomCraftStudio.Studio.Generation;

/// <summary>Kinds of executors a generation can run on (R10.1). Only the local and LAN ComfyUI kinds are implemented.</summary>
public enum ExecutorKind
{
    /// <summary>The self-starting ComfyUI on the platform server.</summary>
    LocalComfy,

    /// <summary>A ComfyUI on the LAN, registered by address.</summary>
    LanComfy,

    /// <summary>Reserved for a cloud ComfyUI service. Not implemented; no request is ever sent to one.</summary>
    CloudComfy,

    /// <summary>Reserved for a cloud image model API. Not implemented; no request is ever sent to one.</summary>
    CloudModelApi
}

/// <summary>One place a generation can run. <see cref="Data"/> is the SwarmUI backend for ComfyUI kinds and would be null for cloud kinds.</summary>
public sealed record class Executor(int Id, string Name, ExecutorKind Kind, BackendHandler.T2IBackendData Data);

/// <summary>Builds the executor list from the registered execution servers. Cloud executors are never produced in this phase.</summary>
public static class ExecutorRegistry
{
    /// <summary>All registered execution servers as executors, regardless of status.</summary>
    public static List<Executor> Current() => [.. ExecutionServers.All.Select(data => new Executor(data.ID, ExecutionServers.DisplayName(data),
        data.Backend is ComfyUISelfStartBackend ? ExecutorKind.LocalComfy : ExecutorKind.LanComfy, data))];
}
