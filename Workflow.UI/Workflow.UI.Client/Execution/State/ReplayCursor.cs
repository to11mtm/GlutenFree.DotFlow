// <copyright file="ReplayCursor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Execution.State;

using System;
using System.Collections.Generic;
using System.Linq;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 🎬 Phase 3.5.5 — Framework-free read-only replay over a finished run's node records (ordered by
/// start). Stepping reveals the run "as of" each node; per-step variable snapshots are post-MVP
/// (3.5.P3). No Blazor/JS types (D2)~ ✨.
/// </summary>
public sealed class ReplayCursor
{
    private readonly List<NodeExecutionRecordDto> ordered;

    /// <summary>Initializes a new instance of the <see cref="ReplayCursor"/> class~ 🎬.</summary>
    /// <param name="records">The node records (any order — sorted by <c>StartedAt</c>).</param>
    public ReplayCursor(IEnumerable<NodeExecutionRecordDto> records)
    {
        this.ordered = records.OrderBy(r => r.StartedAt).ToList();
        this.Step = this.ordered.Count > 0 ? 0 : -1;
    }

    /// <summary>Gets the current 0-based step index (−1 when there are no records).</summary>
    public int Step { get; private set; }

    /// <summary>Gets the total number of steps.</summary>
    public int Count => this.ordered.Count;

    /// <summary>Gets the ordered records.</summary>
    public IReadOnlyList<NodeExecutionRecordDto> Ordered => this.ordered;

    /// <summary>Gets the record at the current step, or null~ 🎯.</summary>
    public NodeExecutionRecordDto? Current
        => this.Step >= 0 && this.Step < this.ordered.Count ? this.ordered[this.Step] : null;

    /// <summary>Gets the records revealed up to and including the current step~ 👁️.</summary>
    public IReadOnlyList<NodeExecutionRecordDto> VisibleNodes
        => this.Step < 0 ? System.Array.Empty<NodeExecutionRecordDto>() : this.ordered.Take(this.Step + 1).ToList();

    /// <summary>Whether a forward step is available.</summary>
    public bool CanStepForward => this.Step < this.ordered.Count - 1;

    /// <summary>Whether a backward step is available.</summary>
    public bool CanStepBack => this.Step > 0;

    /// <summary>Advances one step (clamped)~ ▶️.</summary>
    public void StepForward() => this.SeekTo(this.Step + 1);

    /// <summary>Retreats one step (clamped)~ ◀️.</summary>
    public void StepBack() => this.SeekTo(this.Step - 1);

    /// <summary>Jumps to the first step~ ⏮️.</summary>
    public void First() => this.SeekTo(0);

    /// <summary>Jumps to the last step~ ⏭️.</summary>
    public void Last() => this.SeekTo(this.ordered.Count - 1);

    /// <summary>Seeks to a step (clamped to <c>[0, Count-1]</c>)~ 🎚️.</summary>
    /// <param name="index">The requested step.</param>
    public void SeekTo(int index)
    {
        if (this.ordered.Count == 0)
        {
            this.Step = -1;
            return;
        }

        this.Step = Math.Clamp(index, 0, this.ordered.Count - 1);
    }
}
