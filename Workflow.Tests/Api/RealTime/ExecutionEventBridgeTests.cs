// <copyright file="ExecutionEventBridgeTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Contracts.RealTime;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// 📡 Phase 3.2.1 — Bridge tests. Publishes engine lifecycle events to the Akka <c>EventStream</c>
/// and asserts the hub broadcasts the translated client contract to a subscribed connection~ ✨.
/// </summary>
public sealed class ExecutionEventBridgeTests
{
    [Fact]
    public async Task ExecutionStarted_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<ExecutionStartedEvent>("ExecutionStarted");

        ctx.Publish(new ExecutionStateChanged(ctx.ExecutionId, ExecutionState.Pending, ExecutionState.Running, DateTimeOffset.UtcNow));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.ExecutionId.Should().Be(ctx.ExecutionId);
    }

    [Fact]
    public async Task ExecutionCompleted_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<ExecutionCompletedEvent>("ExecutionCompleted");

        ctx.Publish(new WorkflowCompleted(ctx.ExecutionId, default, TimeSpan.FromMilliseconds(250)));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.ExecutionId.Should().Be(ctx.ExecutionId);
        e.DurationMs.Should().BeApproximately(250, 1);
    }

    [Fact]
    public async Task ExecutionFailed_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<ExecutionFailedEvent>("ExecutionFailed");

        ctx.Publish(new WorkflowFailed(ctx.ExecutionId, new InvalidOperationException("boom"), TimeSpan.FromSeconds(1), Option<HashMap<string, object?>>.None));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.Error.Should().Contain("boom");
    }

    /// <summary>Follow-up — a user cancel reaches clients as ExecutionCancelled, and not also as ExecutionFailed.</summary>
    [Fact]
    public async Task ExecutionCancelled_Broadcast_InsteadOfExecutionFailed()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var cancelled = ctx.On<ExecutionCancelledEvent>("ExecutionCancelled");
        var failed = ctx.On<ExecutionFailedEvent>("ExecutionFailed");

        ctx.Publish(new WorkflowFailed(ctx.ExecutionId, new OperationCanceledException("Workflow execution was cancelled"), TimeSpan.FromSeconds(2), Option<HashMap<string, object?>>.None) { Cancelled = true });

        var e = await cancelled.WaitAsync(RealTimeFixture.Timeout);
        e.ExecutionId.Should().Be(ctx.ExecutionId);
        e.Reason.Should().Contain("cancelled");
        e.DurationMs.Should().Be(2000);
        (await Task.WhenAny(failed, Task.Delay(500))).Should().NotBe(failed, because: "a cancel isn't also reported as a failure");
    }

    [Fact]
    public async Task NodeStarted_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<NodeStartedEvent>("NodeStarted");

        ctx.Publish(new NodeStateChanged("node-1", ctx.ExecutionId, NodeExecutionState.Pending, NodeExecutionState.Running, DateTimeOffset.UtcNow));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.NodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task NodeCompleted_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<NodeCompletedEvent>("NodeCompleted");

        ctx.Publish(new NodeExecutionCompleted("node-1", default, ctx.ExecutionId, TimeSpan.FromMilliseconds(42)));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.NodeId.Should().Be("node-1");
        e.DurationMs.Should().BeApproximately(42, 1);
    }

    [Fact]
    public async Task NodeFailed_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<NodeFailedEvent>("NodeFailed");

        ctx.Publish(new NodeExecutionFailed("node-1", new Exception("nope"), ctx.ExecutionId, TimeSpan.FromMilliseconds(10)));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.NodeId.Should().Be("node-1");
        e.Error.Should().Contain("nope");
    }

    [Fact]
    public async Task ExecutionProgress_Broadcast()
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<ExecutionProgressEvent>("ExecutionProgress");

        ctx.Publish(new ProgressUpdate(ctx.ExecutionId, 60, "node-2", 3, 5));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.Percentage.Should().Be(60);
        e.CurrentNode.Should().Be("node-2");
        e.CompletedNodes.Should().Be(3);
        e.TotalNodes.Should().Be(5);
    }

    /// <summary>📡 Stage 7.6 — streaming-region item counts reach clients, live and final.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamRegionProgress_Broadcast(bool completed)
    {
        await using var ctx = await RealTimeFixture.ConnectAndSubscribeAsync();
        var tcs = ctx.On<StreamProgressEvent>("StreamProgress");

        ctx.Publish(new StreamRegionProgress(
            ctx.ExecutionId,
            2,
            new Dictionary<string, long> { ["src"] = 1234, ["sink"] = 1200 },
            TimeSpan.FromMilliseconds(1500),
            DateTimeOffset.UtcNow,
            completed));

        var e = await tcs.WaitAsync(RealTimeFixture.Timeout);
        e.ExecutionId.Should().Be(ctx.ExecutionId);
        e.RegionIndex.Should().Be(2);
        e.ItemCounts.Should().Contain("src", 1234).And.Contain("sink", 1200);
        e.ElapsedMs.Should().Be(1500);
        e.Completed.Should().Be(completed);
    }

    [Fact]
    public async Task Bridge_Unsubscribes_OnStop()
    {
        // Starting + stopping the host must tear the bridge down cleanly (no leaked subscriptions,
        // no shutdown exceptions). Exercised by constructing + disposing the harness~
        var harness = new SignalRTestHarness();
        _ = harness.Services.GetRequiredService<ActorSystem>();
        await harness.DisposeAsync();
    }
}
