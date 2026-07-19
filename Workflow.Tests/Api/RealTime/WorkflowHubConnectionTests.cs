// <copyright file="WorkflowHubConnectionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.RealTime;
using Xunit;

/// <summary>
/// 📡 Phase 3.2.0 — Hub connection + auth tests. Verifies clients can connect (anonymously when
/// auth is off, with a valid token when on), are rejected without one, and that disconnect cleans
/// up the connection tracker~ ✨.
/// </summary>
public sealed class WorkflowHubConnectionTests
{
    [Fact]
    public async Task Client_CanConnect_WhenAuthDisabled()
    {
        await using var harness = new SignalRTestHarness(requireAuth: false);
        var connection = harness.CreateHubConnection();

        await connection.StartAsync();

        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithValidToken_WhenAuthRequired_Succeeds()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);
        var connection = harness.CreateHubConnection(SignalRTestHarness.MakeJwt(AuthConstants.ViewerRole));

        await connection.StartAsync();

        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithoutToken_WhenAuthRequired_Rejected()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);
        var connection = harness.CreateHubConnection();

        var act = async () => await connection.StartAsync();

        await act.Should().ThrowAsync<Exception>();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_ViaQueryStringToken_WhenAuthRequired_Succeeds()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);
        var connection = harness.CreateHubConnection(
            SignalRTestHarness.MakeJwt(AuthConstants.ViewerRole),
            tokenInQueryString: true);

        await connection.StartAsync();

        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_CleansUpTracker()
    {
        await using var harness = new SignalRTestHarness(requireAuth: false);
        var tracker = harness.Services.GetRequiredService<IConnectionTracker>();
        var connection = harness.CreateHubConnection();

        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);
        tracker.ConnectionCount.Should().BeGreaterThan(0);

        await connection.StopAsync();
        await connection.DisposeAsync();

        // Allow the server-side OnDisconnectedAsync to run~
        await WaitForAsync(() => tracker.ConnectionCount == 0);
        tracker.ConnectionCount.Should().Be(0);
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
