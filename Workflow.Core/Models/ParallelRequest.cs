// uwu~ ParallelRequest: the kawaii sibling of LoopRequest 💖
// Returned by a module (via ModuleResult.WithParallel) to tell the engine
// "hey~ please fan these branches out concurrently and ping me when done!"
namespace Workflow.Core.Models;

/// <summary>
/// Describes a parallel fan-out request emitted by a module.
/// Mirrors <see cref="LoopRequest"/> but for concurrent branch execution.
/// </summary>
/// <remarks>
/// CopilotNotes: The <see cref="BranchPorts"/> list is the authoritative
/// ordering of branches; branch index N corresponds to <c>BranchPorts[N]</c>
/// and is the port the engine will activate to seed that branch's sub-graph.
/// </remarks>
public sealed class ParallelRequest
{
    /// <summary>
    /// Output port names that fan out into independent branches.
    /// Each port becomes one concurrent <c>SubGraphExecutor</c>.
    /// </summary>
    /// <remarks>
    /// CopilotNote: 2.2.3a (static fan-out): one branch per entry. Ignored when
    /// <see cref="Items"/> is set (per-item fan-out mode used by <c>FanOutModule</c>)~ 🌐
    /// </remarks>
    public required IReadOnlyList<string> BranchPorts { get; init; }

    /// <summary>
    /// Phase 2.2.3b: Per-item fan-out collection. When non-null, the coordinator spawns
    /// one sub-graph per item (all routed through <see cref="BranchPort"/>), seeding each
    /// branch with <c>item</c> and <c>index</c> inputs. Mutually exclusive with <see cref="BranchPorts"/>.
    /// </summary>
    public IReadOnlyList<object?>? Items { get; init; }

    /// <summary>
    /// Phase 2.2.3b: Single output port used by per-item fan-out. Required when
    /// <see cref="Items"/> is non-null. Default: <c>"branch"</c>.
    /// </summary>
    public string BranchPort { get; init; } = "branch";

    /// <summary>
    /// Maximum number of branches running concurrently. Default: unbounded.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = int.MaxValue;

    /// <summary>
    /// When <c>true</c> (default), the first branch failure cancels siblings
    /// via cooperative cancellation and reports failure to the parent.
    /// </summary>
    public bool FailFast { get; init; } = true;

    /// <summary>
    /// When <c>true</c> (default), waits for all branches before completing.
    /// <c>false</c> (deferred to 2.2.3b) would complete on first success.
    /// </summary>
    public bool WaitForAll { get; init; } = true;

    /// <summary>
    /// Port to fire on successful completion of all branches. Default: "done".
    /// </summary>
    public string DonePort { get; init; } = "done";

    /// <summary>
    /// Optional aggregated outputs to pass through to the done-port edges.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? AggregatedOutputs { get; init; }
}

