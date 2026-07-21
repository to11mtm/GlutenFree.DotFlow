// <copyright file="ConnectionMetricsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.RealTime;
using Xunit;

/// <summary>
/// 📡 Phase 3.2.3 — Connection/subscription metrics tests: tracker gauges move with connect /
/// disconnect / subscribe, and the monitoring endpoints surface them~ ✨.
/// </summary>
public sealed class ConnectionMetricsTests
{
    [Fact]
    public async Task ActiveConnections_IncrementsOnConnect_DecrementsOnDisconnect()
    {
        await using var harness = new SignalRTestHarness();
        var tracker = harness.Services.GetRequiredService<IConnectionTracker>();
        tracker.ConnectionCount.Should().Be(0);

        var connection = harness.CreateHubConnection();
        await connection.StartAsync();
        await WaitForAsync(() => tracker.ConnectionCount == 1);
        tracker.ConnectionCount.Should().Be(1);

        await connection.StopAsync();
        await connection.DisposeAsync();
        await WaitForAsync(() => tracker.ConnectionCount == 0);
        tracker.ConnectionCount.Should().Be(0);
    }

    [Fact]
    public async Task ActiveSubscriptions_TracksSubUnsub()
    {
        await using var harness = new SignalRTestHarness();
        var tracker = harness.Services.GetRequiredService<IConnectionTracker>();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var execId = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", execId);
        await WaitForAsync(() => tracker.SubscriptionCount == 1);
        tracker.SubscriptionCount.Should().Be(1);

        await connection.InvokeAsync("UnsubscribeFromExecution", execId);
        await WaitForAsync(() => tracker.SubscriptionCount == 0);
        tracker.SubscriptionCount.Should().Be(0);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task MultipleConnections_PerUser_CountedIndependently()
    {
        await using var harness = new SignalRTestHarness();
        var tracker = harness.Services.GetRequiredService<IConnectionTracker>();

        var c1 = harness.CreateHubConnection();
        var c2 = harness.CreateHubConnection();
        await c1.StartAsync();
        await c2.StartAsync();

        await WaitForAsync(() => tracker.ConnectionCount == 2);
        tracker.ConnectionCount.Should().Be(2);

        await c1.DisposeAsync();
        await c2.DisposeAsync();
    }

    [Fact]
    public async Task Metrics_Endpoint_IncludesRealtimeGauges()
    {
        await using var harness = new SignalRTestHarness();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync("SubscribeToExecution", Guid.NewGuid());

        var client = harness.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/v1/metrics");

        doc.TryGetProperty("realtime_connections_active", out var conns).Should().BeTrue();
        conns.GetInt64().Should().BeGreaterThan(0);
        doc.TryGetProperty("realtime_subscriptions_active", out _).Should().BeTrue();

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Status_Endpoint_IncludesConnectionCount()
    {
        await using var harness = new SignalRTestHarness();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var client = harness.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/v1/status");

        doc.TryGetProperty("activeConnections", out _).Should().BeTrue();
        doc.TryGetProperty("activeSubscriptions", out _).Should().BeTrue();

        await connection.DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var start = DateTime.UtcNow;
        while (!predicate() && (DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            await Task.Delay(50);
        }
    }
}
