// <copyright file="SpillableItemBuffer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Streaming;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;

/// <summary>
/// 🪣 Phase 5.1.6 — an item buffer that keeps the first N items in memory and spills the rest.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The shape here is "memory head, spilled tail". Items are appended in order, so the
/// in-memory prefix always precedes the spilled remainder and replay is simply <i>memory, then
/// spill</i> — no merge, no sort, no sequence bookkeeping. Getting that ordering wrong would be
/// invisible in small tests and catastrophic in the exact large runs spill exists to serve~ 🎯.
/// </para>
/// <para>
/// The memory threshold is not a second ceiling to tune against the guard: below it spill costs
/// nothing, so a job that never grows big never touches disk at all. Above it memory stops growing
/// entirely~ 📏.
/// </para>
/// <para>
/// This is a <b>bounded</b> feature, not an unbounded one. The accumulator's ceiling still applies;
/// spill changes <i>where</i> the items live, not whether there's a limit. See
/// <c>phases/Phase5-1-6-SpillMode-Plan.md</c>~ 🛡️.
/// </para>
/// </remarks>
public sealed class SpillableItemBuffer : IAsyncDisposable
{
    /// <summary>
    /// How many items stay in memory before spilling begins~ 📏.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Deliberately modest. The cost of being wrong upward is an OOM; the cost of
    /// being wrong downward is some IO on a job that was going to do IO anyway~ ⚖️
    /// </remarks>
    public const int DefaultMemoryItemThreshold = 10_000;

    private readonly List<StreamItem> memory = [];
    private readonly ISpillStore store;
    private readonly ISpillProtector protector;
    private readonly SpillSessionKey key;
    private readonly int memoryThreshold;
    private readonly ILogger? logger;

    private ISpillSession? session;
    private long spilledCount;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpillableItemBuffer"/> class~ 🪣.
    /// </summary>
    /// <param name="store">Where to spill once memory fills.</param>
    /// <param name="protector">Encrypts spilled records; use <see cref="NoOpSpillProtector"/> to opt out.</param>
    /// <param name="key">Identifies this stage's scratch.</param>
    /// <param name="memoryItemThreshold">How many items to keep in memory; defaults to <see cref="DefaultMemoryItemThreshold"/>.</param>
    /// <param name="logger">Optional logger, used for the encryption-disabled warning.</param>
    public SpillableItemBuffer(
        ISpillStore store,
        ISpillProtector protector,
        SpillSessionKey key,
        int? memoryItemThreshold = null,
        ILogger? logger = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
        this.key = key;
        this.memoryThreshold = memoryItemThreshold ?? DefaultMemoryItemThreshold;
        this.logger = logger;

        if (this.memoryThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(memoryItemThreshold),
                "The in-memory threshold can't be negative~ 📏");
        }
    }

    /// <summary>Gets the total number of items added~ 🔢.</summary>
    public long Count { get; private set; }

    /// <summary>Gets how many items were written to the spill store~ 💾.</summary>
    public long SpilledCount => this.spilledCount;

    /// <summary>Gets a value indicating whether anything has actually been spilled~ 💾.</summary>
    public bool HasSpilled => this.session is not null;

    /// <summary>
    /// Adds one item, spilling it if the memory budget is already used up~ ⬇️.
    /// </summary>
    /// <param name="item">The item to buffer.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes once the item is buffered.</returns>
    public async Task AddAsync(StreamItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.Count++;

        if (this.memory.Count < this.memoryThreshold && this.session is null)
        {
            this.memory.Add(item);
            return;
        }

        if (this.session is null)
        {
            this.session = await this.store.BeginAsync(this.key, ct).ConfigureAwait(false);

            // 📢 Q-D: every execution that actually spills says so when encryption is off.
            if (!this.protector.IsEnabled)
            {
                this.logger?.LogWarning(
                    "⚠️ Node {NodeId} is spilling stream data to '{Store}' **unencrypted** — spill encryption is disabled on this host. Workflow data is being written at rest in the clear~",
                    this.key.NodeId,
                    this.store.Name);
            }
            else
            {
                this.logger?.LogInformation(
                    "💾 Node {NodeId} exceeded its {Threshold}-item memory budget and is spilling to '{Store}'~",
                    this.key.NodeId,
                    this.memoryThreshold,
                    this.store.Name);
            }
        }

        var bytes = StreamItemSerializer.Serialize(item);
        var stored = this.protector.Protect(bytes);
        await this.session.AppendAsync(stored, ct).ConfigureAwait(false);
        this.spilledCount++;
    }

    /// <summary>
    /// Replays every buffered item in insertion order~ ⬆️.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The items, memory-resident ones first.</returns>
    public async IAsyncEnumerable<StreamItem> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        foreach (var item in this.memory)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }

        if (this.session is null)
        {
            yield break;
        }

        await this.session.FlushAsync(ct).ConfigureAwait(false);

        await foreach (var stored in this.session.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var bytes = this.protector.Unprotect(stored);
            yield return StreamItemSerializer.Deserialize(bytes);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.memory.Clear();

        if (this.session is not null)
        {
            // CopilotNote: eager cleanup on the happy path — the background sweeper is a safety net
            // for crashes, not the primary reclaim mechanism~ 🧹
            await this.session.DisposeAsync().ConfigureAwait(false);
            this.session = null;
        }
    }
}
