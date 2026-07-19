// <copyright file="ReconnectionTests.cs" company="GlutenFree">
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
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Xunit;

/// <summary>
/// 📡 Phase 3.2.4 — Reconnection + resilience: after a drop the client re-subscribes and resumes
/// receiving live events, an expired token is rejected, and broadcasting to a gone client is safe~ ✨.
/// </summary>
public sealed class ReconnectionTests
{
    private static ExecutionStateChanged Started(Guid execId)
        => new(execId, ExecutionState.Pending, ExecutionState.Running, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Reconnect_RestoresSubscriptions_ViaClientReinvoke_ReceivesLiveEventsAgain()
    {
        await using var harness = new SignalRTestHarness();
        var system = harness.Services.GetRequiredService<ActorSystem>();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var execId = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", execId);

        // Simulate a drop: stop then start again (a new connection id server-side)~
        await connection.StopAsync();
        await connection.StartAsync();

        // Client-driven re-subscribe (the documented reconnect contract — server holds no durable state)~
        await connection.InvokeAsync("SubscribeToExecution", execId);

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<ExecutionStartedEvent>("ExecutionStarted", e => received.TrySetResult(e.ExecutionId));

        system.EventStream.Publish(Started(execId));
        var got = await received.Task.WaitAsync(RealTimeFixture.Timeout);
        got.Should().Be(execId);

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Reconnect_WithExpiredToken_FailsCleanly()
    {
        await using var harness = new SignalRTestHarness(requireAuth: true);

        // A token that is already expired must be rejected at connect time~
        var expired = MakeExpiredJwt();
        var connection = harness.CreateHubConnection(expired);

        var act = async () => await connection.StartAsync();
        await act.Should().ThrowAsync<Exception>();

        await connection.DisposeAsync();
    }

    [Fact]
    public async Task ServerSend_ToDroppedClient_DoesNotThrow()
    {
        await using var harness = new SignalRTestHarness();
        var system = harness.Services.GetRequiredService<ActorSystem>();
        var connection = harness.CreateHubConnection();
        await connection.StartAsync();

        var execId = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", execId);

        // Drop the client, then publish — the bridge must swallow the failed send and stay alive~
        await connection.StopAsync();
        await connection.DisposeAsync();

        system.EventStream.Publish(Started(execId));

        // Bridge still processes further events (no crash) — a fresh client can still connect + receive~
        var c2 = harness.CreateHubConnection();
        await c2.StartAsync();
        var execId2 = Guid.NewGuid();
        await c2.InvokeAsync("SubscribeToExecution", execId2);

        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        c2.On<ExecutionStartedEvent>("ExecutionStarted", e => received.TrySetResult(e.ExecutionId));
        system.EventStream.Publish(Started(execId2));

        var got = await received.Task.WaitAsync(RealTimeFixture.Timeout);
        got.Should().Be(execId2);

        await c2.DisposeAsync();
    }

    private static string MakeExpiredJwt()
    {
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(SignalRTestHarness.JwtSigningKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: SignalRTestHarness.JwtIssuer,
            audience: SignalRTestHarness.JwtAudience,
            claims: new[]
            {
                new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, "expired-user"),
                new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, AuthConstants.ViewerRole),
            },
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
