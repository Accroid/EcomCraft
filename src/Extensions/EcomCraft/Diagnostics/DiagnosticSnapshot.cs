namespace EcomCraftStudio.Diagnostics;

/// <summary>Non-secret host configuration facts; no paths, credentials or user records.</summary>
public record HostObservation(bool AuthenticationRequired, bool LocalhostBypass, bool UserOutputIsolation, bool AutomaticUpdates, bool PortCanChange);

/// <summary>An engine's reported state, not a health probe, approved capability or scheduling authorization.</summary>
public record ExecutionObservation(int BackendId, int? RemoteBackendId, string BackendType, string EngineStatus,
    bool Enabled, bool ShutdownReserved, int Reservations, bool ModelLoadReserved, int Usages, int MaxUsages, bool CanLoadModels);

/// <summary>A configured host backend and its observed executors. BackendId is not a stable EcomCraft node ID.</summary>
public record NodeObservation(int BackendId, string Name, string BackendType, string Kind, ExecutionObservation Engine,
    bool? AllowForwarding, IReadOnlyList<ExecutionObservation> ExecutionBackends, bool ObservationFailed = false);

/// <summary>A scoped configuration check. Passed checks never imply a passed live acceptance test.</summary>
public record DiagnosticCheck(string Id, string Status, string Summary, string Detail);

/// <summary>A required live verification gate, with no fabricated execution evidence.</summary>
public record SpikeCheck(string Id, string Title, string Status, string Evidence);

/// <summary>A conservative assessment that cannot declare the platform production-ready.</summary>
public record DiagnosticReport(string M0Status, IReadOnlyList<DiagnosticCheck> Checks, IReadOnlyList<SpikeCheck> Spikes);
