using EcomCraftStudio.Diagnostics;

namespace EcomCraftStudio.Tests;

/// <summary>Dependency-free checks for the actual production evaluator; never starts SwarmUI.</summary>
public static class TestProgram
{
    /// <summary>Number of successful assertions.</summary>
    private static int Assertions;

    /// <summary>Runs conservative readiness and capacity checks.</summary>
    public static int Main()
    {
        HostObservation safe = new(true, false, true, false, false);
        NodeObservation remote = RemoteNode();
        DiagnosticReport baseline = ReadinessEvaluator.Evaluate(safe, [remote, remote with { BackendId = 2 }]);
        Check(baseline.M0Status == "pending_verification", "safe snapshots never prove M0 readiness");
        Check(baseline.Spikes.Count == 6, "all six spikes are present");
        Check(baseline.Spikes.All(s => s.Status == "pending_verification"), "no spike is auto-passed");
        Check(baseline.Checks.Single(c => c.Id == "remote_node_count").Status == "pending_verification", "two configs do not prove two PCs");
        Check(ReadinessEvaluator.Evaluate(safe with { AuthenticationRequired = false }, [remote]).M0Status == "blocked", "authentication is required");
        Check(ReadinessEvaluator.Evaluate(safe with { LocalhostBypass = true }, [remote]).M0Status == "blocked", "localhost bypass blocks");
        Check(ReadinessEvaluator.Evaluate(safe with { UserOutputIsolation = false }, [remote]).M0Status == "blocked", "user output isolation is required");
        Check(ReadinessEvaluator.Evaluate(safe with { AutomaticUpdates = true }, [remote]).M0Status == "blocked", "automatic updates block");
        Check(ReadinessEvaluator.Evaluate(safe, []).M0Status == "blocked", "no configured nodes blocks");
        Check(ReadinessEvaluator.Evaluate(safe with { PortCanChange = true }, [remote]).Checks.Any(c => c.Id == "fixed_port" && c.Status == "warning"), "port changes are a warning, not proof of a broken proxy");
        Check(ReadinessEvaluator.Evaluate(safe, [remote with { AllowForwarding = true }]).M0Status == "blocked", "enabled forwarding blocks");
        Check(ReadinessEvaluator.Evaluate(safe, [remote with { AllowForwarding = null }]).M0Status == "blocked", "unknown forwarding fails closed");
        Check(ReadinessEvaluator.Evaluate(safe, [remote with { ObservationFailed = true }]).M0Status == "blocked", "failed observations block");
        Check(ReadinessEvaluator.EngineReportsCapacity(remote), "running known direct executor reports only engine capacity");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { AllowForwarding = true }), "forwarding is not local capacity");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { AllowForwarding = null }), "unknown forwarding is not capacity");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { ExecutionBackends = [] }), "control backend cannot generate alone");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { ObservationFailed = true }), "failed snapshot cannot report capacity");
        foreach (string status in new[] { "idle", "disabled", "errored", "loading", "waiting", "unknown", "future_status" })
        {
            Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { Engine = remote.Engine with { EngineStatus = status } }), $"parent {status} prevents child capacity");
            Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { ExecutionBackends = [Executor() with { EngineStatus = status }] }), $"executor {status} is unavailable");
        }
        ExecutionObservation[] unavailable =
        [
            Executor() with { Enabled = false },
            Executor() with { ShutdownReserved = true },
            Executor() with { Reservations = 1 },
            Executor() with { ModelLoadReserved = true },
            Executor() with { Usages = 1 },
            Executor() with { Usages = -1 },
            Executor() with { MaxUsages = 0 },
            Executor() with { CanLoadModels = false },
            Executor() with { BackendType = "swarmswarmbackend" },
            Executor() with { BackendType = "unknown" }
        ];
        foreach (ExecutionObservation executor in unavailable)
        {
            Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { ExecutionBackends = [executor] }), "unavailable or unsupported executor is rejected");
        }
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { Engine = remote.Engine with { Enabled = false } }), "disabled parent excludes children");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { Engine = remote.Engine with { ShutdownReserved = true } }), "shutdown parent excludes children");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { Engine = remote.Engine with { Reservations = 1 } }), "reserved parent excludes children");
        Check(!ReadinessEvaluator.EngineReportsCapacity(remote with { Kind = "unsupported" }), "unsupported node does not report capacity");
        NodeObservation local = new(3, "Local test", "comfyui_selfstart", "local_comfy", Executor(), null, []);
        Check(ReadinessEvaluator.EngineReportsCapacity(local), "known local engine can report capacity");
        Check(!ReadinessEvaluator.EngineReportsCapacity(local with { Engine = Executor() with { Usages = 1 } }), "full local engine is unavailable");
        Check(ReadinessEvaluator.Evaluate(safe, [local]).M0Status == "pending_verification", "local engine does not prove remote or policy readiness");
        Console.WriteLine($"PASS: {Assertions} assertions; no host, GPU, network or user data accessed.");
        return 0;
    }

    /// <summary>Builds a remote control observation with one directly connected Comfy executor.</summary>
    private static NodeObservation RemoteNode()
    {
        return new(1, "Remote test", "swarmswarmbackend", "remote_swarm", Executor() with { BackendId = 1, CanLoadModels = false }, false, [Executor()]);
    }

    /// <summary>Builds a single free, known execution backend.</summary>
    private static ExecutionObservation Executor()
    {
        return new(-1, 4, "comfyui_selfstart", "running", true, false, 0, false, 0, 1, true);
    }

    /// <summary>Fails the process with an actionable description when an assertion is false.</summary>
    private static void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException(description);
        }
        Assertions++;
    }
}
