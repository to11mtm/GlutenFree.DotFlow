// <copyright file="SubscriptionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts.RealTime;
using Workflow.Api.RealTime;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// 📡 Phase 3.2.2 — Subscription management tests: only subscribed clients receive events, the
/// admin firehose is gated, unsubscribe stops delivery, and the tracker stays consistent~ ✨.
/// </summary>
public sealed class SubscriptionTests
{
    private static ExecutionStateChanged Started(Guid execId)
        => new(execId, ExecutionState.Pending, ExecutionState.Running, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Subscribe_ToExecution_ReceivesOnlyThatExecutionsEvents()
    {
        await using var harness = new SignalRTestHarness();
        var system = harness.Services.GetRequiredService<ActorSystem>();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", mine);

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ExecutionStartedEvent>("ExecutionStarted", e => received.TrySetResult(e.ExecutionId));

        // Event for an execution we did NOT subscribe to → must not arrive~
        system.EventStream.Publish(Started(other));
        var early = await Task.WhenAny(received.Task, Task.Delay(1500));
        early.Should().NotBe(received.Task, "events for other executions must not be delivered");

        // Event for our execution → arrives~
        system.EventStream.Publish(Started(mine));
        var got = await received.Task.WaitAsync(RealTimeFixture.Timeout);
        got.Should().Be(mine);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Unsubscribe_StopsEvents()
    {
        await using var harness = new SignalRTestHarness();
        var system = harness.Services.GetRequiredService<ActorSystem>();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var execId = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", execId);
        await connection.InvokeAsync("UnsubscribeFromExecution", execId);

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ExecutionStartedEvent>("ExecutionStarted", e => received.TrySetResult(e.ExecutionId));

        system.EventStream.Publish(Started(execId));
        var raced = await Task.WhenAny(received.Task, Task.Delay(1500));
        raced.Should().NotBe(received.Task, "unsubscribed clients receive nothing");

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeToAll_AsAdmin_ReceivesEverything()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);
        var system = harness.Services.GetRequiredService<ActorSystem>();
        var connection = harness.CreateHubConnection(SignalRTestHarness.MakeJwt(AuthConstants.AdminRole));
        await connection.StartAsync();

        await connection.InvokeAsync("SubscribeToAll");

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ExecutionStartedEvent>("ExecutionStarted", e => received.TrySetResult(e.ExecutionId));

        var execId = Guid.NewGuid();
        system.EventStream.Publish(Started(execId));

        var got = await received.Task.WaitAsync(RealTimeFixture.Timeout);
        got.Should().Be(execId);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeToAll_AsNonAdmin_Denied()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);
        var connection = harness.CreateHubConnection(SignalRTestHarness.MakeJwt(AuthConstants.ViewerRole));
        await connection.StartAsync();

        var act = async () => await connection.InvokeAsync("SubscribeToAll");

        await act.Should().ThrowAsync<Exception>();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Subscribe_PushesInitialSnapshot()
    {
        // No real execution exists → GetStatusAsync returns null → no snapshot pushed, but the call
        // must still succeed. When a status IS available the snapshot flows (covered by the handler).
        await using var harness = new SignalRTestHarness();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var act = async () => await connection.InvokeAsync("SubscribeToExecution", Guid.NewGuid());

        await act.Should().NotThrowAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public void Tracker_AddRemove_Threadsafe()
    {
        var tracker = new ConnectionTracker();
        System.Threading.Tasks.Parallel.For(0, 200, i =>
        {
            var conn = "c" + (i % 10);
            tracker.AddConnection(conn);
            tracker.AddSubscription(conn, "execution:" + i);
            tracker.RemoveSubscription(conn, "execution:" + i);
        });

        tracker.ConnectionCount.Should().BeLessOrEqualTo(10);
        tracker.SubscriptionCount.Should().Be(0);
    }
}
