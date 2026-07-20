// <copyright file="RealTimeClient.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Workflow.UI.Client.Api.Dtos;

/// <summary>
/// 📡 Phase 3.3.a.0 — Thin wrapper over the SignalR <c>HubConnection</c> to the Phase 3.2
/// <c>/hubs/workflow</c> hub. Exposes typed C# events for the run overlay (3.3.c) and auto
/// re-subscribes on reconnect (3.2 D9). Framework-free (no Blazor types)~ ✨.
/// </summary>
public sealed class RealTimeClient : IAsyncDisposable
{
    private readonly ApiClientOptions options;
    private readonly AuthState auth;
    private HubConnection? connection;
    private Guid? subscribedExecution;

    /// <summary>Initializes a new instance of the <see cref="RealTimeClient"/> class~ 📡.</summary>
    /// <param name="options">The API options (for the hub URL).</param>
    /// <param name="auth">The auth state (for the access token).</param>
    public RealTimeClient(ApiClientOptions options, AuthState auth)
    {
        this.options = options;
        this.auth = auth;
    }

    /// <summary>Raised when an execution starts~ 🚀.</summary>
    public event Action<ExecutionStartedEvent>? ExecutionStarted;

    /// <summary>Raised when an execution completes~ 🎊.</summary>
    public event Action<ExecutionCompletedEvent>? ExecutionCompleted;

    /// <summary>Raised when an execution fails~ 😿.</summary>
    public event Action<ExecutionFailedEvent>? ExecutionFailed;

    /// <summary>Raised when a node starts~ ⚡.</summary>
    public event Action<NodeStartedEvent>? NodeStarted;

    /// <summary>Raised when a node completes~ ✅.</summary>
    public event Action<NodeCompletedEvent>? NodeCompleted;

    /// <summary>Raised when a node fails~ ⚠️.</summary>
    public event Action<NodeFailedEvent>? NodeFailed;

    /// <summary>Raised on progress updates~ 💫.</summary>
    public event Action<ExecutionProgressEvent>? ExecutionProgress;

    /// <summary>Raised with the initial snapshot after subscribing~ 📸.</summary>
    public event Action<ExecutionSnapshotEvent>? ExecutionSnapshot;

    /// <summary>Raised when the connection drops and is retrying~ 🔁.</summary>
    public event Action? Reconnecting;

    /// <summary>Raised when the connection is re-established~ 🟢.</summary>
    public event Action? Reconnected;

    /// <summary>Gets a value indicating whether the hub is currently connected.</summary>
    public bool IsConnected => this.connection?.State == HubConnectionState.Connected;

    /// <summary>Connects to the hub (idempotent)~ 🔌.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (this.connection is not null)
        {
            if (this.connection.State == HubConnectionState.Disconnected)
            {
                await this.connection.StartAsync(ct).ConfigureAwait(false);
            }

            return;
        }

        var builder = new HubConnectionBuilder()
            .WithUrl(this.options.HubUrl, o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(this.auth.Token);
            })
            .WithAutomaticReconnect();

        this.connection = builder.Build();
        this.RegisterHandlers(this.connection);

        this.connection.Reconnecting += _ =>
        {
            this.Reconnecting?.Invoke();
            return Task.CompletedTask;
        };
        this.connection.Reconnected += async _ =>
        {
            // Re-subscribe: the server holds no durable per-connection state across reconnects (3.2 D9)~
            if (this.subscribedExecution is Guid execId)
            {
                await this.connection!.InvokeAsync("SubscribeToExecution", execId).ConfigureAwait(false);
            }

            this.Reconnected?.Invoke();
        };

        await this.connection.StartAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Subscribes to a specific execution's events~ ⚡.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SubscribeToExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        await this.ConnectAsync(ct).ConfigureAwait(false);
        this.subscribedExecution = executionId;
        await this.connection!.InvokeAsync("SubscribeToExecution", executionId, ct).ConfigureAwait(false);
    }

    /// <summary>Unsubscribes from the currently-subscribed execution, if any~ ⚡.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task UnsubscribeAsync(CancellationToken ct = default)
    {
        if (this.connection is not null && this.subscribedExecution is Guid execId && this.IsConnected)
        {
            await this.connection.InvokeAsync("UnsubscribeFromExecution", execId, ct).ConfigureAwait(false);
        }

        this.subscribedExecution = null;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (this.connection is not null)
        {
            await this.connection.DisposeAsync().ConfigureAwait(false);
            this.connection = null;
        }
    }

    private void RegisterHandlers(HubConnection conn)
    {
        conn.On<ExecutionStartedEvent>("ExecutionStarted", e => this.ExecutionStarted?.Invoke(e));
        conn.On<ExecutionCompletedEvent>("ExecutionCompleted", e => this.ExecutionCompleted?.Invoke(e));
        conn.On<ExecutionFailedEvent>("ExecutionFailed", e => this.ExecutionFailed?.Invoke(e));
        conn.On<NodeStartedEvent>("NodeStarted", e => this.NodeStarted?.Invoke(e));
        conn.On<NodeCompletedEvent>("NodeCompleted", e => this.NodeCompleted?.Invoke(e));
        conn.On<NodeFailedEvent>("NodeFailed", e => this.NodeFailed?.Invoke(e));
        conn.On<ExecutionProgressEvent>("ExecutionProgress", e => this.ExecutionProgress?.Invoke(e));
        conn.On<ExecutionSnapshotEvent>("ExecutionSnapshot", e => this.ExecutionSnapshot?.Invoke(e));
    }
}
