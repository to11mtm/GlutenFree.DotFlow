// <copyright file="WorkflowLaunchException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using System;

/// <summary>
///  Phase 2.3.9 — Represents a failure that occurred while trying to launch a workflow
/// execution in response to a webhook trigger~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Thrown by <see cref="ActorWorkflowLauncher"/> when:
/// <list type="bullet">
///   <item>The workflow definition ID in the registration is not found in the repository.</item>
///   <item>The <see cref="Workflow.Engine.Actors.WorkflowSupervisor"/> returns a
///         <see cref="Workflow.Engine.Messages.WorkflowInstanceCreationFailed"/> response.</item>
///   <item>The ask-pattern call to the supervisor times out.</item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: <see cref="WebhookDispatcher"/> catches this and returns
/// <c>500 Internal Server Error</c> with a sanitised message — the full exception (including
/// <see cref="WebhookId"/> and <see cref="WorkflowDefinitionId"/>) is written to the structured
/// log so operators have context without leaking internals to callers~
/// </para>
/// </remarks>
public sealed class WorkflowLaunchException : Exception
{
    /// <summary>
    /// Initialises a new instance of <see cref="WorkflowLaunchException"/>~ .
    /// </summary>
    /// <param name="webhookId">The webhook slug that triggered the launch attempt.</param>
    /// <param name="workflowDefinitionId">The workflow definition ID that was being launched.</param>
    /// <param name="message">Human-readable failure description.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public WorkflowLaunchException(
        string webhookId,
        Guid workflowDefinitionId,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        WebhookId = webhookId;
        WorkflowDefinitionId = workflowDefinitionId;
    }

    /// <summary>Gets the webhook slug that triggered the failed launch attempt~ .</summary>
    public string WebhookId { get; }

    /// <summary>Gets the workflow definition ID that could not be instantiated~ .</summary>
    public Guid WorkflowDefinitionId { get; }
}
