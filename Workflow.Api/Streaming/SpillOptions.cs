// <copyright file="SpillOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;

/// <summary>
/// ⚙️ Host configuration for streaming spill (Phase 5.1.6), bound from <c>Streaming:Spill</c>.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: There is <b>no implicit default store</b>. Leaving <see cref="Store"/> as
/// <see cref="SpillStoreKind.None"/> means a workflow that asks to spill fails with a message
/// naming this setting. That is the point — a silent fallback to a temp directory would land
/// scratch on whatever filesystem the container happened to have, which on a small cloud node is
/// both slow and unprovisioned (plan §11)~ 🚧.
/// </para>
/// <para>
/// <b>Which store?</b> (plan §13, Q-C1) <see cref="SpillStoreKind.Sqlite"/> is the recommended
/// <i>local</i> option — one provisionable file, lowest latency, no network hop. It does <b>not</b>
/// escape a slow local device, though, because it still consumes local IOPS. On hosts with
/// slow or throttled local disk, use <see cref="SpillStoreKind.Blob"/> so scratch leaves the node
/// for infrastructure an operator already sizes (Postgres / NATS / S3)~ 🎯.
/// </para>
/// </remarks>
public sealed class SpillOptions
{
    /// <summary>The configuration section name~ 🔧.</summary>
    public const string SectionName = "Streaming:Spill";

    /// <summary>
    /// Gets or sets which spill store to use. Defaults to <see cref="SpillStoreKind.None"/>~ 💾.
    /// </summary>
    public SpillStoreKind Store { get; set; } = SpillStoreKind.None;

    /// <summary>
    /// Gets or sets the SQLite spill file path (required when <see cref="Store"/> is
    /// <see cref="SpillStoreKind.Sqlite"/>)~ 🗄️.
    /// </summary>
    public string? SqlitePath { get; set; }

    /// <summary>
    /// Gets or sets the directory used when <see cref="Store"/> is
    /// <see cref="SpillStoreKind.Directory"/>~ 📁.
    /// </summary>
    public string? DirectoryPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether spilled data is encrypted at rest. Default <c>true</c>~ 🔒.
    /// </summary>
    /// <remarks>
    /// Turning this off is allowed but never quiet: startup logs a warning naming this setting,
    /// every spilling execution logs a warning, and the designer surfaces it on stages where spill
    /// could engage (plan §10, Q-D)~ 📢.
    /// </remarks>
    public bool Encrypt { get; set; } = true;

    /// <summary>
    /// Gets or sets how many items a buffering stage keeps in memory before spilling~ 📏.
    /// </summary>
    public int MemoryItemThreshold { get; set; }
        = Workflow.Modules.Streaming.SpillableItemBuffer.DefaultMemoryItemThreshold;

    /// <summary>
    /// Gets or sets how long spill survives after its execution reaches a terminal state~ 🧹.
    /// </summary>
    public TimeSpan RetentionAfterCompletion { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets how long a <b>non-terminal</b> execution's record may go without an update
    /// before its spill is treated as abandoned and reclaimed~ 🪦.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This closes the crash hole in "never delete spill for a running job" (plan §13,
    /// Q-E1). A host that dies mid-execution leaves its record stuck non-terminal forever, so
    /// without a second threshold that spill would never be reclaimed. Set conservatively — days,
    /// not minutes — because deleting a genuinely-running job's scratch would fail the job~ ⚖️.
    /// </remarks>
    public TimeSpan AbandonedThreshold { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// Gets or sets how often the background sweeper runs~ ⏱️.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>
/// 💾 Which spill backend the host uses.
/// </summary>
public enum SpillStoreKind
{
    /// <summary>
    /// No spill store — <c>spill: true</c> fails loudly. The default~ 🚧.
    /// </summary>
    None,

    /// <summary>
    /// One configured SQLite file. The recommended <b>local</b> option~ 🗄️.
    /// </summary>
    Sqlite,

    /// <summary>
    /// The configured <c>IBlobStore</c> (Postgres / NATS / S3). Recommended for hosts whose local
    /// disk is slow or throttled~ ☁️.
    /// </summary>
    Blob,

    /// <summary>
    /// A plain directory of scratch files. Explicit opt-in only~ 📁.
    /// </summary>
    Directory,
}
