// <copyright file="ISpillStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Streaming;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💾 Phase 5.1.6 — durable scratch space for a buffering streaming stage that outgrows memory.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is <b>scratch</b>, not persistence. Nothing here needs to survive a restart —
/// if the host dies mid-execution the execution is dead too, and the spill is garbage. That single
/// fact is what lets implementations turn off durability knobs (SQLite <c>synchronous=OFF</c>) and
/// go dramatically faster than a general-purpose store~ 🏎️.
/// </para>
/// <para>
/// <b>There is deliberately no default implementation.</b> A stage configured with
/// <c>spill: true</c> on a host with no spill store fails loudly rather than quietly landing in
/// a temp directory nobody provisioned. See <c>phases/Phase5-1-6-SpillMode-Plan.md</c> §11~ 🚧.
/// </para>
/// <para>
/// <b>Choosing a store</b> (plan §13, Q-C1):
/// <list type="bullet">
///   <item><description>fast local disk → the SQLite spill store: lowest latency, no network hop;</description></item>
///   <item><description>slow or throttled local disk (small cloud nodes, overlay filesystems) →
///     the blob spill store, so scratch leaves the node for infrastructure an operator already
///     sizes. A SQLite file still consumes local IOPS — it fixes bloat and configurability, not
///     a slow device.</description></item>
/// </list>
/// </para>
/// </remarks>
public interface ISpillStore
{
    /// <summary>
    /// Gets a short name for this store, used in log and error messages~ 🏷️.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Opens a spill session for one buffering stage of one execution~ 📂.
    /// </summary>
    /// <param name="key">Identifies the owning execution, region and node.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A session that accepts appends and can replay them once.</returns>
    Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default);

    /// <summary>
    /// Deletes every artefact belonging to the given execution~ 🧹.
    /// </summary>
    /// <param name="executionId">The execution whose spill should be reclaimed.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>How many artefacts were removed.</returns>
    Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default);

    /// <summary>
    /// Lists the distinct execution ids that currently hold spill artefacts~ 📃.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The execution ids, in unspecified order.</returns>
    /// <remarks>
    /// CopilotNote: The cleanup service walks this and asks the execution history whether each one
    /// is reclaimable. Returning ids rather than artefacts keeps the sweep cheap — one repository
    /// lookup per execution, not per segment~ 🧹
    /// </remarks>
    IAsyncEnumerable<Guid> ListExecutionsAsync(CancellationToken ct = default);
}

/// <summary>
/// 📂 One stage's spill scratch: append many, then replay once.
/// </summary>
/// <remarks>
/// CopilotNote: The append-then-replay shape is not an accident — it's the only access pattern a
/// buffering stage has, and restricting the interface to it means implementations never need random
/// access, indexes, or an update path. Disposing removes the artefacts eagerly; the background
/// sweeper exists only for the cases where dispose never runs~ 🧹
/// </remarks>
public interface ISpillSession : IAsyncDisposable
{
    /// <summary>Gets how many records have been appended~ 🔢.</summary>
    long Count { get; }

    /// <summary>Gets the total bytes written (after protection, if enabled)~ 📏.</summary>
    long BytesWritten { get; }

    /// <summary>
    /// Appends one serialized record~ ⬇️.
    /// </summary>
    /// <param name="payload">The record bytes.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the record is durable enough to replay.</returns>
    Task AppendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default);

    /// <summary>
    /// Flushes any pending buffered appends~ 🚿.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when everything appended is replayable.</returns>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>
    /// Replays every appended record in append order~ ⬆️.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The records, in the order they were appended.</returns>
    IAsyncEnumerable<byte[]> ReadAllAsync(CancellationToken ct = default);
}

/// <summary>
/// 🔑 Identifies one stage's spill scratch within one execution.
/// </summary>
/// <param name="ExecutionId">The owning execution — the unit retention is keyed to. 🆔.</param>
/// <param name="RegionIndex">The streaming region ordinal within that execution. 🌊.</param>
/// <param name="NodeId">The buffering node's id. 🧩.</param>
/// <remarks>
/// CopilotNote: <see cref="ExecutionId"/> leads the key on purpose — every retention rule in the
/// plan is expressed per execution, and putting it first makes "delete everything for execution X"
/// a single prefix operation on every backing store~ 🧹
/// </remarks>
public readonly record struct SpillSessionKey(Guid ExecutionId, int RegionIndex, string NodeId)
{
    /// <summary>
    /// Gets the key prefix covering every artefact of one execution~ 🧹.
    /// </summary>
    /// <param name="executionId">The execution id.</param>
    /// <returns>A prefix string.</returns>
    public static string PrefixFor(Guid executionId)
        => $"spill/{executionId:N}/";

    /// <summary>
    /// Gets the key prefix covering every artefact of this session~ 📂.
    /// </summary>
    /// <returns>A prefix string.</returns>
    public string Prefix()
        => $"{PrefixFor(ExecutionId)}{RegionIndex}/{Sanitize(NodeId)}/";

    private static string Sanitize(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            return "_";
        }

        // CopilotNote: node ids are user-authored, so they can contain anything. Keys land in
        // filesystem paths and object stores, so restrict to a boring alphabet rather than
        // discovering the hard way which backend chokes on which character~ 🔒
        Span<char> buffer = stackalloc char[nodeId.Length];
        for (var i = 0; i < nodeId.Length; i++)
        {
            var c = nodeId[i];
            buffer[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }

        return new string(buffer);
    }
}

/// <summary>
/// 🚧 Thrown when spill is requested but the host has no spill store configured.
/// </summary>
/// <remarks>
/// CopilotNote: A distinct type so the region executor can turn it into a configuration error
/// naming the setting, rather than a generic node failure the operator has to guess about~ 🔧
/// </remarks>
public sealed class SpillNotConfiguredException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="SpillNotConfiguredException"/> class.</summary>
    public SpillNotConfiguredException()
        : this("Spill is enabled on this node but no spill store is configured on this host~ 💾")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SpillNotConfiguredException"/> class.</summary>
    /// <param name="message">The message.</param>
    public SpillNotConfiguredException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SpillNotConfiguredException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SpillNotConfiguredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
