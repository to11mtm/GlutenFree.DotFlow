// <copyright file="WorkflowSupervisor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Engine.Messages;

/// <summary>
/// Top-level supervisor actor responsible for managing workflow lifecycle.
/// This actor creates and supervises WorkflowExecutor actors for each workflow instance~ 🎭✨
/// </summary>
/// <remarks>
/// <para>
/// The WorkflowSupervisor is the entry point for all workflow operations. It maintains
/// a registry of active workflow executions and routes messages to the appropriate
/// WorkflowExecutor actors.
/// </para>
/// <para>
/// CopilotNote: This actor uses the "one-actor-per-workflow" pattern for isolation.
/// Each workflow execution gets its own WorkflowExecutor actor as a child, which
/// enables independent failure handling and resource management~ 💖
/// </para>
/// </remarks>
public class WorkflowSupervisor : ReceiveActor
{
    private readonly ILoggingAdapter log;
    private readonly IServiceProvider serviceProvider;
    private readonly Dictionary<Guid, IActorRef> activeWorkflows;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowSupervisor"/> class.
    /// Sets up message handlers and supervision strategy~ UwU
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection.</param>
    public WorkflowSupervisor(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        this.log = Context.GetLogger();
        this.activeWorkflows = new Dictionary<Guid, IActorRef>();

        // Handle workflow instance creation
        Receive<CreateWorkflowInstance>(msg => HandleCreateWorkflowInstance(msg));

        // Handle workflow status queries
        Receive<GetWorkflowStatus>(msg => HandleGetWorkflowStatus(msg));

        // Handle workflow cancellation
        Receive<CancelExecution>(msg => HandleCancelExecution(msg));

        // Handle child actor termination (death watch)
        Receive<Terminated>(msg => HandleTerminated(msg));

        this.log.Info("🎭 WorkflowSupervisor started and ready to manage workflows! UwU");
    }

    /// <summary>
    /// Creates Props for spawning a WorkflowSupervisor actor.
    /// Use this factory method for proper dependency injection~ 💝
    /// </summary>
    /// <param name="serviceProvider">Service provider for DI.</param>
    /// <returns>Props configuration for actor creation.</returns>
    public static Props Props(IServiceProvider serviceProvider)
    {
        return Akka.Actor.Props.Create(() => new WorkflowSupervisor(serviceProvider));
    }

    /// <summary>
    /// Configures the supervision strategy for child WorkflowExecutor actors.
    /// Implements resilient error handling with restart limits~ 🛡️
    /// </summary>
    /// <returns>The supervision strategy.</returns>
    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            localOnlyDecider: ex =>
            {
                switch (ex)
                {
                    // Transient failures - restart the actor
                    case TimeoutException:
                    case System.IO.IOException:
                        this.log.Warning(
                            "⚠️ Transient failure in workflow executor: {ErrorType}. Restarting...",
                            ex.GetType().Name);
                        return Directive.Restart;

                    // Critical failures - stop the actor
                    case InvalidOperationException:
                    case ArgumentException:
                        this.log.Error(
                            ex,
                            "❌ Critical failure in workflow executor: {ErrorType}. Stopping actor.",
                            ex.GetType().Name);
                        return Directive.Stop;

                    // Unknown failures - escalate to parent
                    default:
                        this.log.Error(
                            ex,
                            "🔥 Unknown failure in workflow executor: {ErrorType}. Escalating...",
                            ex.GetType().Name);
                        return Directive.Escalate;
                }
            });
    }

    /// <summary>
    /// Lifecycle hook called when the actor is starting.
    /// Performs initialization tasks~ 🌸
    /// </summary>
    protected override void PreStart()
    {
        base.PreStart();
        this.log.Info("✨ WorkflowSupervisor initializing...");
    }

    /// <summary>
    /// Lifecycle hook called when the actor is stopping.
    /// Performs cleanup tasks~ 🧹
    /// </summary>
    protected override void PostStop()
    {
        this.log.Info(
            "👋 WorkflowSupervisor stopping. Active workflows: {Count}",
            this.activeWorkflows.Count);

        // Cancel all active workflows on shutdown
        foreach (var (executionId, executor) in this.activeWorkflows)
        {
            this.log.Info("🛑 Cancelling workflow {ExecutionId} due to supervisor shutdown", executionId);
            executor.Tell(new CancelExecution(executionId));
        }

        this.activeWorkflows.Clear();
        base.PostStop();
    }

    /// <summary>
    /// Handles the CreateWorkflowInstance message.
    /// Creates a new WorkflowExecutor child actor and starts the workflow~ 🚀
    /// </summary>
    /// <param name="message">The create workflow message.</param>
    private void HandleCreateWorkflowInstance(CreateWorkflowInstance message)
    {
        try
        {
            this.log.Info(
                "📝 Creating workflow instance for workflow {WorkflowId}: {WorkflowName}",
                message.WorkflowId,
                message.Definition.Name);

            // Validate workflow definition
            var validator = this.serviceProvider.GetRequiredService<Workflow.Core.Abstractions.WorkflowValidator>();
            var validationResult = validator.Validate(message.Definition);

            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join(", ", validationResult.Errors.Select(e => e.ToString()));
                this.log.Error(
                    "❌ Workflow validation failed for {WorkflowId}: {Errors}",
                    message.WorkflowId,
                    errorMessages);

                Sender.Tell(new Status.Failure(
                    new InvalidOperationException($"Workflow validation failed: {errorMessages}")));
                return;
            }

            // Generate unique execution ID
            var executionId = Guid.NewGuid();

            // Create child WorkflowExecutor actor
            var executorName = $"executor-{executionId:N}";
            var executor = Context.ActorOf(
                WorkflowExecutor.Props(executionId, message.Definition, message.Inputs, this.serviceProvider),
                executorName);

            // Watch for child termination
            Context.Watch(executor);

            // Store actor reference
            this.activeWorkflows[executionId] = executor;

            this.log.Info(
                "✅ Workflow executor created for execution {ExecutionId}. Total active: {Count}",
                executionId,
                this.activeWorkflows.Count);

            // Reply with execution ID
            Sender.Tell(new WorkflowInstanceCreated(executionId, message.WorkflowId));

            // Automatically start execution
            executor.Tell(new StartExecution(executionId));
        }
        catch (Exception ex)
        {
            this.log.Error(ex, "💥 Failed to create workflow instance: {Error}", ex.Message);
            Sender.Tell(new Status.Failure(ex));
        }
    }

    /// <summary>
    /// Handles the GetWorkflowStatus message.
    /// Forwards the status request to the appropriate WorkflowExecutor~ 📊
    /// </summary>
    /// <param name="message">The status query message.</param>
    private void HandleGetWorkflowStatus(GetWorkflowStatus message)
    {
        if (this.activeWorkflows.TryGetValue(message.ExecutionId, out var executor))
        {
            this.log.Debug("🔍 Forwarding status request for execution {ExecutionId}", message.ExecutionId);
            executor.Forward(message);
        }
        else
        {
            this.log.Warning("⚠️ Status requested for unknown execution {ExecutionId}", message.ExecutionId);
            Sender.Tell(new Status.Failure(
                new InvalidOperationException($"Execution {message.ExecutionId} not found")));
        }
    }

    /// <summary>
    /// Handles the CancelExecution message.
    /// Forwards the cancellation request to the appropriate WorkflowExecutor~ 🛑
    /// </summary>
    /// <param name="message">The cancel execution message.</param>
    private void HandleCancelExecution(CancelExecution message)
    {
        if (this.activeWorkflows.TryGetValue(message.ExecutionId, out var executor))
        {
            this.log.Info("🛑 Forwarding cancellation request for execution {ExecutionId}", message.ExecutionId);
            executor.Forward(message);
        }
        else
        {
            this.log.Warning("⚠️ Cancellation requested for unknown execution {ExecutionId}", message.ExecutionId);
            Sender.Tell(new Status.Failure(
                new InvalidOperationException($"Execution {message.ExecutionId} not found")));
        }
    }

    /// <summary>
    /// Handles the Terminated message when a child WorkflowExecutor stops.
    /// Cleans up tracking data and logs termination~ 💔
    /// </summary>
    /// <param name="message">The termination message.</param>
    private void HandleTerminated(Terminated message)
    {
        // Find and remove the terminated executor
        Guid? terminatedExecutionId = null;
        foreach (var (executionId, executor) in this.activeWorkflows)
        {
            if (executor.Equals(message.ActorRef))
            {
                terminatedExecutionId = executionId;
                break;
            }
        }

        if (terminatedExecutionId.HasValue)
        {
            this.activeWorkflows.Remove(terminatedExecutionId.Value);
            this.log.Info(
                "💀 Workflow executor terminated for execution {ExecutionId}. Active workflows: {Count}",
                terminatedExecutionId.Value,
                this.activeWorkflows.Count);
        }
        else
        {
            this.log.Warning("⚠️ Received Terminated message for unknown actor: {ActorPath}", message.ActorRef.Path);
        }
    }
}

