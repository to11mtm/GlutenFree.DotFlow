// <copyright file="WorkflowHub.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.RealTime;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Workflow.Api.Auth;
using Workflow.Api.Contracts.RealTime;
using Workflow.Api.Execution;

/// <summary>
/// 📡 Phase 3.2 — The real-time hub. Clients connect (authenticated via the existing
/// <see cref="AuthConstants.WorkflowReadPolicy"/>, header or query-string token) and subscribe to
/// specific workflows/executions (or, if admin, to everything). The engine's lifecycle events are
/// pushed here by <see cref="ExecutionEventBridge"/>~ ✨.
/// </summary>
[Authorize(AuthConstants.WorkflowReadPolicy)]
public sealed class WorkflowHub : Hub<IWorkflowHubClient>
{
    private readonly IConnectionTracker tracker;
    private readonly IAuthorizationService authorization;
    private readonly ILogger<WorkflowHub> logger;
    private readonly IServiceProvider services;

    /// <summary>Initializes a new instance of the <see cref="WorkflowHub"/> class~ 📡.</summary>
    /// <param name="tracker">The connection tracker.</param>
    /// <param name="authorization">The authorization service (for the admin-gated firehose).</param>
    /// <param name="services">The root service provider (for the optional status snapshot).</param>
    /// <param name="logger">The logger.</param>
    public WorkflowHub(
        IConnectionTracker tracker,
        IAuthorizationService authorization,
        IServiceProvider services,
        ILogger<WorkflowHub> logger)
    {
        this.tracker = tracker;
        this.authorization = authorization;
        this.services = services;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public override Task OnConnectedAsync()
    {
        this.tracker.AddConnection(this.Context.ConnectionId);
        this.logger.LogDebug("📡 Client connected: {ConnectionId}~", this.Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    /// <inheritdoc/>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Groups are auto-removed by SignalR on disconnect; clear the tracker mirror too~
        this.tracker.RemoveConnection(this.Context.ConnectionId);
        this.logger.LogDebug("📡 Client disconnected: {ConnectionId}~", this.Context.ConnectionId);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>Subscribes the caller to all events of a specific workflow~ 📋.</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <returns>A task.</returns>
    public Task SubscribeToWorkflow(Guid workflowId)
        => this.SubscribeAsync(RealTimeGroups.Workflow(workflowId));

    /// <summary>Unsubscribes the caller from a workflow~ 📋.</summary>
    /// <param name="workflowId">The workflow id.</param>
    /// <returns>A task.</returns>
    public Task UnsubscribeFromWorkflow(Guid workflowId)
        => this.UnsubscribeAsync(RealTimeGroups.Workflow(workflowId));

    /// <summary>Subscribes the caller to a specific execution, then pushes an initial snapshot~ ⚡.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <returns>A task.</returns>
    public async Task SubscribeToExecution(Guid executionId)
    {
        await this.SubscribeAsync(RealTimeGroups.Execution(executionId)).ConfigureAwait(false);
        await this.PushSnapshotAsync(executionId).ConfigureAwait(false);
    }

    /// <summary>Unsubscribes the caller from an execution~ ⚡.</summary>
    /// <param name="executionId">The execution id.</param>
    /// <returns>A task.</returns>
    public Task UnsubscribeFromExecution(Guid executionId)
        => this.UnsubscribeAsync(RealTimeGroups.Execution(executionId));

    /// <summary>Subscribes the caller to the firehose (all events). Requires the admin policy~ 🌐.</summary>
    /// <returns>A task.</returns>
    public async Task SubscribeToAll()
    {
        var result = await this.authorization
            .AuthorizeAsync(this.Context.User ?? new System.Security.Claims.ClaimsPrincipal(), null, AuthConstants.AdminPolicy)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new HubException("SubscribeToAll requires the admin policy.");
        }

        await this.SubscribeAsync(RealTimeGroups.All).ConfigureAwait(false);
    }

    /// <summary>Unsubscribes the caller from the firehose~ 🌐.</summary>
    /// <returns>A task.</returns>
    public Task UnsubscribeFromAll()
        => this.UnsubscribeAsync(RealTimeGroups.All);

    private async Task SubscribeAsync(string group)
    {
        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, group).ConfigureAwait(false);
        this.tracker.AddSubscription(this.Context.ConnectionId, group);
        this.logger.LogDebug("📡 {ConnectionId} subscribed to {Group}~", this.Context.ConnectionId, group);
    }

    private async Task UnsubscribeAsync(string group)
    {
        await this.Groups.RemoveFromGroupAsync(this.Context.ConnectionId, group).ConfigureAwait(false);
        this.tracker.RemoveSubscription(this.Context.ConnectionId, group);
        this.logger.LogDebug("📡 {ConnectionId} unsubscribed from {Group}~", this.Context.ConnectionId, group);
    }

    private async Task PushSnapshotAsync(Guid executionId)
    {
        var service = this.services.GetService(typeof(IWorkflowExecutionService)) as IWorkflowExecutionService;
        if (service is null)
        {
            return;
        }

        try
        {
            var status = await service.GetStatusAsync(executionId, this.Context.ConnectionAborted).ConfigureAwait(false);
            if (status is null)
            {
                return;
            }

            var snapshot = new ExecutionSnapshotEvent(
                status.ExecutionId,
                status.State.ToString(),
                status.Progress,
                status.NodeStates ?? new Dictionary<string, string>(),
                status.EndTime,
                status.Error);

            await this.Clients.Caller.ExecutionSnapshot(snapshot).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A snapshot failure must never break the subscription~
            this.logger.LogDebug(ex, "📡 Snapshot push failed for {ExecutionId}~", executionId);
        }
    }
}
