// <copyright file="ReplayCursorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.State;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 🎬 Phase 3.5.5 — Tests for the framework-free <see cref="ReplayCursor"/>~ ✨.
/// </summary>
public sealed class ReplayCursorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-20T16:00:00Z");

    private static NodeExecutionRecordDto Node(string id, int order)
        => new(id, "Completed", T0.AddSeconds(order), T0.AddSeconds(order + 1), 1000, null, null, null, null, null);

    private static ReplayCursor Cursor()
        => new(new[] { Node("c", 2), Node("a", 0), Node("b", 1) }); // out of order on purpose

    [Fact]
    public void Cursor_OrdersByStart_AndStartsAtFirst()
    {
        var c = Cursor();
        c.Count.Should().Be(3);
        c.Step.Should().Be(0);
        c.Current!.NodeId.Should().Be("a");
        c.VisibleNodes.Should().ContainSingle();
    }

    [Fact]
    public void Cursor_Step_RevealsNodesUpTo()
    {
        var c = Cursor();
        c.StepForward();
        c.Current!.NodeId.Should().Be("b");
        c.VisibleNodes.Should().HaveCount(2);
        c.VisibleNodes[^1].NodeId.Should().Be("b");
    }

    [Fact]
    public void Cursor_SeekTo_Jumps()
    {
        var c = Cursor();
        c.SeekTo(2);
        c.Current!.NodeId.Should().Be("c");
        c.VisibleNodes.Should().HaveCount(3);
    }

    [Fact]
    public void Cursor_Bounds_Clamp()
    {
        var c = Cursor();
        c.StepBack();
        c.Step.Should().Be(0);
        c.CanStepBack.Should().BeFalse();

        c.Last();
        c.Step.Should().Be(2);
        c.StepForward();
        c.Step.Should().Be(2);
        c.CanStepForward.Should().BeFalse();
    }

    [Fact]
    public void Cursor_Empty_HasNoCurrent()
    {
        var c = new ReplayCursor(System.Array.Empty<NodeExecutionRecordDto>());
        c.Count.Should().Be(0);
        c.Step.Should().Be(-1);
        c.Current.Should().BeNull();
        c.VisibleNodes.Should().BeEmpty();
    }
}
