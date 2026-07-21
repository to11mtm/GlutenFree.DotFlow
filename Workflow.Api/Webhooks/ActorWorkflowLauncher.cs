// <copyright file="ActorWorkflowLauncher.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Persistence.Abstractions;

/// <summary>
///  Phase 2.3.9 — Production <see cref="IWorkflowLauncher"/> that dispatches workflow
/// executions through the Akka.NET <c>WorkflowSupervisor</c> actor~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <see cref="NullWorkflowLauncher"/> as the default in production DI. Steps:
/// <list type="number">
///   <item>Load <see cref="WorkflowDefinition"/> from <see cref="IWorkflowRepository"/> by
///         <see cref="WebhookRegistration.WorkflowDefinitionId"/>.</item>
///   <item>Send <see cref="CreateWorkflowInstance"/> to the supervisor via Akka <c>Ask</c>.</item>
///   <item>Return the <see cref="WorkflowInstanceCreated.ExecutionId"/> from the response.</item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: All failure paths throw <see cref="WorkflowLaunchException"/> so
/// <see cref="WebhookDispatcher"/> can return a clean 500 without leaking actor internals~
/// </para>
/// </remarks>
public sealed class ActorWorkflowLauncher : IWorkflowLauncher
{
    private static readonly TimeSpan DefaultAskTimeout = TimeSpan.FromSeconds(15);

    private readonly WorkflowSupervisorActorRef _supervisorRef;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly ILogger<ActorWorkflowLauncher> _logger;

    /// <summary>
    /// Initialises the launcher~ .
    /// </summary>
    /// <param name="supervisorRef">DI wrapper around the supervisor actor reference.</param>
    /// <param name="workflowRepo">Repository used to load the workflow definition by ID.</param>
    /// <param name="logger">Logger for operation traces and error details.</param>
    public ActorWorkflowLauncher(
        WorkflowSupervisorActorRef supervisorRef,
        IWorkflowRepository workflowRepo,
        ILogger<ActorWorkflowLauncher> logger)
    {
        _supervisorRef = supervisorRef ?? throw new ArgumentNullException(nameof(supervisorRef));
        _workflowRepo = workflowRepo ?? throw new ArgumentNullException(nameof(workflowRepo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Loads the workflow definition, sends <see cref="CreateWorkflowInstance"/> to the supervisor,
    /// and returns the execution ID~ .
    /// </remarks>
    public async Task<Guid> LaunchAsync(
        WebhookRegistration registration,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct = default)
    {
        // 1️⃣ Load workflow definition~
        WorkflowDefinition? definition;
        try
        {
            definition = await _workflowRepo
                .GetByIdAsync(registration.WorkflowDefinitionId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                " Failed to load workflow definition {DefinitionId} for webhook '{WebhookId}'~",
                registration.WorkflowDefinitionId, registration.WebhookId);
            throw new WorkflowLaunchException(
                registration.WebhookId,
                registration.WorkflowDefinitionId,
                $"Failed to load workflow definition '{registration.WorkflowDefinitionId}'.",
                ex);
        }

        if (definition is null)
        {
            _logger.LogWarning(
                " Workflow definition {DefinitionId} not found for webhook '{WebhookId}'~",
                registration.WorkflowDefinitionId, registration.WebhookId);
            throw new WorkflowLaunchException(
                registration.WebhookId,
                registration.WorkflowDefinitionId,
                $"Workflow definition '{registration.WorkflowDefinitionId}' was not found.");
        }

        // 2️⃣ Convert inputs to HashMap (Akka messages use immutable LanguageExt collections)~
        var inputMap = inputs.Aggregate(
            HashMap<string, object?>.Empty,
            (acc, kv) => acc.Add(kv.Key, kv.Value));

        // 3️⃣ Ask the supervisor to create the workflow instance~
        IWorkflowMessage response;
        try
        {
            // CopilotNote: Akka Ask with CancellationToken requires Akka.Streams or timeout override.
            // Using the overload with TimeSpan + a linked CTS covers both the CT and timeout cases~
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(DefaultAskTimeout);

            response = await _supervisorRef.ActorRef
                .Ask<IWorkflowMessage>(
                    new CreateWorkflowInstance(definition.Id, definition, inputMap),
                    cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Timed out (not cancelled by caller)~
            _logger.LogError(
                " WorkflowSupervisor Ask timed out for workflow {DefinitionId} / webhook '{WebhookId}'~",
                registration.WorkflowDefinitionId, registration.WebhookId);
            throw new WorkflowLaunchException(
                registration.WebhookId,
                registration.WorkflowDefinitionId,
                "WorkflowSupervisor did not respond in time.",
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                " WorkflowSupervisor Ask failed for workflow {DefinitionId} / webhook '{WebhookId}'~",
                registration.WorkflowDefinitionId, registration.WebhookId);
            throw new WorkflowLaunchException(
                registration.WebhookId,
                registration.WorkflowDefinitionId,
                $"Failed to communicate with WorkflowSupervisor: {ex.Message}",
                ex);
        }

        // 4️⃣ Unpack the response~
        switch (response)
        {
            case WorkflowInstanceCreated created:
                _logger.LogInformation(
                    " Workflow {DefinitionId} launched as execution {ExecutionId} via webhook '{WebhookId}'~",
                    registration.WorkflowDefinitionId, created.ExecutionId, registration.WebhookId);
                return created.ExecutionId;

            case WorkflowInstanceCreationFailed failed:
                throw new WorkflowLaunchException(
                    registration.WebhookId,
                    registration.WorkflowDefinitionId,
                    $"WorkflowSupervisor rejected the workflow instance: {string.Join("; ", failed.Errors)}");

            default:
                throw new WorkflowLaunchException(
                    registration.WebhookId,
                    registration.WorkflowDefinitionId,
                    $"Unexpected supervisor response type: {response.GetType().Name}");
        }
    }
}

