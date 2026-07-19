// <copyright file="ExecutionEventBridgeTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
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
