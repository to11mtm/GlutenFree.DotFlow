// <copyright file="MonitorStateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.State;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 🖥️ Phase 3.5.2 — Tests for the framework-free <see cref="MonitorState"/> live-merge~ ✨.
/// </summary>
public sealed class MonitorStateTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-20T16:00:00Z");

    [Fact]
    public void Merge_StartedThenProgress_UpsertsRunningRow()
    {
        var state = new MonitorState();
        var id = Guid.NewGuid();
        var wf = Guid.NewGuid();

        state.ApplyStarted(new ExecutionStartedEvent(id, wf, T0));
        state.ApplyProgress(new ExecutionProgressEvent(id, 63, "enrich", 2, 4, T0.AddSeconds(1)));

        var row = state.Find(id);
        row.Should().NotBeNull();
        row!.IsRunning.Should().BeTrue();
        row.Progress.Should().Be(63);
        row.CurrentNode.Should().Be("enrich");
        row.WorkflowId.Should().Be(wf);
        state.Running.Should().ContainSingle();
        state.Recent().Should().BeEmpty();
    }

    [Fact]
    public void Merge_Completed_MovesToRecent()
    {
        var state = new MonitorState();
        var id = Guid.NewGuid();
        state.ApplyStarted(new ExecutionStartedEvent(id, Guid.NewGuid(), T0));

        state.ApplyCompleted(new ExecutionCompletedEvent(id, Guid.NewGuid(), 1400, T0.AddSeconds(1.4)));

        state.Running.Should().BeEmpty();
        var row = state.Recent().Single();
        row.State.Should().Be("Completed");
        row.Progress.Should().Be(100);
        row.DurationMs.Should().Be(1400);
    }

    [Fact]
    public void Merge_Failed_ShowsError()
    {
        var state = new MonitorState();
        var id = Guid.NewGuid();
        state.ApplyStarted(new ExecutionStartedEvent(id, null, T0));

        state.ApplyFailed(new ExecutionFailedEvent(id, null, "TimeoutError", 900, T0.AddSeconds(0.9)));

        var row = state.Recent().Single();
        row.State.Should().Be("Failed");
        row.Error.Should().Be("TimeoutError");
        row.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void SeedFromList_ThenLiveEvent_Merges_NoDuplicates()
    {
        var state = new MonitorState();
        var id = Guid.NewGuid();
        var wf = Guid.NewGuid();

        state.SeedFromList(new[]
        {
            new ExecutionDto(id, wf, "Running", T0, null, "alice"),
        });
        state.Running.Should().ContainSingle();

        // A live progress event for the same id updates in place (no new row).
        state.ApplyProgress(new ExecutionProgressEvent(id, 50, "n2", 1, 2, T0.AddSeconds(1)));
        state.ApplyCompleted(new ExecutionCompletedEvent(id, wf, 500, T0.AddSeconds(2)));

        state.Rows.Should().ContainSingle();
        state.Recent().Single().State.Should().Be("Completed");
    }

    [Fact]
    public void Recent_Sort_ByDuration_Orders()
    {
        var state = new MonitorState();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        state.SeedFromList(new[]
        {
            new ExecutionDto(a, Guid.NewGuid(), "Completed", T0, T0.AddSeconds(1), null),
            new ExecutionDto(b, Guid.NewGuid(), "Completed", T0, T0.AddSeconds(5), null),
        });

        var byDurAsc = state.Recent("Duration", descending: false);
        byDurAsc.First().ExecutionId.Should().Be(a);
        byDurAsc.Last().ExecutionId.Should().Be(b);
    }

    [Fact]
    public void Changed_Raised_OnMerge()
    {
        var state = new MonitorState();
        var raised = 0;
        state.Changed += () => raised++;

        state.ApplyStarted(new ExecutionStartedEvent(Guid.NewGuid(), null, T0));

        raised.Should().Be(1);
    }
}
