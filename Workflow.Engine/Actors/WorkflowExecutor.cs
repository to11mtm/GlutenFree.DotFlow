// <copyright file="WorkflowExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using Akka.Event;

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using Akka.Actor;
using Workflow.Core.Models;
using Workflow.Engine.Messages;

/// <summary>
/// Actor responsible for executing a single workflow instance.
/// This is a STUB implementation - will be completed in Phase 1.3.2~ 🎬
/// </summary>
/// <remarks>
/// CopilotNote: This is a minimal stub to allow WorkflowSupervisor to compile.
/// Full implementation will be added in section 1.3.2 of the sub-phase plan~ 💖
/// </remarks>
public class WorkflowExecutor : ReceiveActor
{
    private readonly Guid executionId;
    private readonly WorkflowDefinition definition;
    private readonly Dictionary<string, object?> inputs;
    private readonly IServiceProvider serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowExecutor"/> class.
    /// </summary>
    /// <param name="executionId">The unique execution ID.</param>
    /// <param name="definition">The workflow definition to execute.</param>
    /// <param name="inputs">Initial input values.</param>
    /// <param name="serviceProvider">Service provider for DI.</param>
    public WorkflowExecutor(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider)
    {
        this.executionId = executionId;
        this.definition = definition;
        this.inputs = inputs;
        this.serviceProvider = serviceProvider;

        // Stub message handlers - will be implemented in 1.3.2
        Receive<StartExecution>(msg => HandleStartExecution(msg));
        Receive<GetWorkflowStatus>(msg => HandleGetWorkflowStatus(msg));
        Receive<CancelExecution>(msg => HandleCancelExecution(msg));
    }

    /// <summary>
    /// Creates Props for spawning a WorkflowExecutor actor.
    /// </summary>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="definition">The workflow definition.</param>
    /// <param name="inputs">Initial inputs.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <returns>Props configuration.</returns>
    public static Props Props(
        Guid executionId,
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        IServiceProvider serviceProvider)
    {
        return Akka.Actor.Props.Create(
            () => new WorkflowExecutor(executionId, definition, inputs, serviceProvider));
    }

    private void HandleStartExecution(StartExecution message)
    {
        // Stub - will be implemented in 1.3.2
        var log = Context.GetLogger();
        log.Info("🎬 WorkflowExecutor stub: StartExecution received for {ExecutionId}", this.executionId);
    }

    private void HandleGetWorkflowStatus(GetWorkflowStatus message)
    {
        // Stub - return minimal status
        Sender.Tell(new WorkflowStatusResponse(
            this.executionId,
            ExecutionState.Pending,
            0,
            new Dictionary<string, NodeExecutionState>(),
            DateTimeOffset.UtcNow,
            null,
            null));
    }

    private void HandleCancelExecution(CancelExecution message)
    {
        // Stub - will be implemented in 1.3.2
        var log = Context.GetLogger();
        log.Info("🛑 WorkflowExecutor stub: CancelExecution received for {ExecutionId}", this.executionId);
    }
}

