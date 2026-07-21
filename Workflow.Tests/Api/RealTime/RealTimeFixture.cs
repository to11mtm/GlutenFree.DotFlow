// <copyright file="RealTimeFixture.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.RealTime;

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 📡 Phase 3.2 — Test helper wiring a started hub connection to a harness, with helpers to
/// publish engine events onto the Akka <c>EventStream</c> and await typed client callbacks~ ✨.
/// </summary>
public sealed class RealTimeFixture : IAsyncDisposable
{
    /// <summary>The wait budget for a broadcast to arrive~ ⏱️.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly SignalRTestHarness harness;
    private readonly ActorSystem actorSystem;

    private RealTimeFixture(SignalRTestHarness harness, HubConnection connection, Guid executionId)
    {
        this.harness = harness;
        this.Connection = connection;
        this.ExecutionId = executionId;
        this.actorSystem = harness.Services.GetRequiredService<ActorSystem>();
    }

    /// <summary>Gets the live hub connection~ 🔌.</summary>
    public HubConnection Connection { get; }

    /// <summary>Gets the execution id this fixture is subscribed to~ 🆔.</summary>
    public Guid ExecutionId { get; }

    /// <summary>Connects a client and subscribes it to a fresh execution id~ 🔗.</summary>
    /// <param name="requireAuth">Whether to enforce auth.</param>
    /// <param name="role">The role for the minted token (when auth is required).</param>
    /// <returns>A ready fixture.</returns>
    public static async Task<RealTimeFixture> ConnectAndSubscribeAsync(bool requireAuth = false, string? role = null)
    {
        var harness = new SignalRTestHarness(requireAuth);
        var token = requireAuth ? SignalRTestHarness.MakeJwt(role ?? Workflow.Api.Auth.AuthConstants.ViewerRole) : null;
        var connection = harness.CreateHubConnection(token);
        await connection.StartAsync();

        var executionId = Guid.NewGuid();
        await connection.InvokeAsync("SubscribeToExecution", executionId);

        return new RealTimeFixture(harness, connection, executionId);
    }

    /// <summary>Registers a one-shot handler for a hub client method and returns its completion~ 🎯.</summary>
    /// <typeparam name="T">The event payload type.</typeparam>
    /// <param name="method">The hub client method name.</param>
    /// <returns>A task that completes with the first received payload.</returns>
    public Task<T> On<T>(string method)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.Connection.On<T>(method, payload => tcs.TrySetResult(payload));
        return tcs.Task;
    }

    /// <summary>Publishes an engine message onto the Akka <c>EventStream</c>~ 📤.</summary>
    /// <param name="message">The message.</param>
    public void Publish(object message) => this.actorSystem.EventStream.Publish(message);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await this.Connection.DisposeAsync();
        }
        catch
        {
            // best-effort~
        }

        await this.harness.DisposeAsync();
    }
}
