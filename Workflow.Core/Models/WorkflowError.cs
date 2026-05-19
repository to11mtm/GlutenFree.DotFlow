// <copyright file="WorkflowError.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

using System;

/// <summary>
/// 🛡️ Structured error value captured when a workflow node fails inside a try/catch boundary~
/// Serialisable to JSON and passed as the <c>error</c> output of <c>builtin.trycatch</c>~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.4 — created by <c>TryCatchExecutorActor</c> when a sub-graph failure
/// is caught. Passed as the <c>error</c> input to the catch-branch entry node so catch handlers
/// can inspect <see cref="ErrorType"/>, <see cref="Message"/>, and <see cref="Data"/>~ 🌸.
/// </para>
/// <para>
/// <see cref="FromException"/> is the primary factory: it auto-sets <see cref="OccurredAt"/>
/// and extracts <see cref="ErrorType"/> from the exception type name (or <c>WorkflowUserError.ErrorType</c>)~
/// </para>
/// </remarks>
/// <param name="ErrorType">Classification string for the error (e.g. "ValidationError", "TimeoutError")~ 🏷️.</param>
/// <param name="Message">Human-readable error message~ 💬.</param>
/// <param name="NodeId">ID of the node that threw the error, if known~ 🆔.</param>
/// <param name="Data">Arbitrary structured data attached to the error (optional)~ 📦.</param>
/// <param name="OccurredAt">UTC timestamp when the error occurred~ ⏱️.</param>
/// <param name="StackTrace">Optional stack trace string for debugging~ 🔍.</param>
public sealed record WorkflowError(
    string ErrorType,
    string Message,
    string? NodeId,
    object? Data,
    DateTimeOffset OccurredAt,
    string? StackTrace = null)
{
    /// <summary>
    /// Builds a <see cref="WorkflowError"/> from an exception~ 🏭✨
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <param name="nodeId">Optional node ID where the exception occurred.</param>
    /// <returns>A new <see cref="WorkflowError"/> populated from the exception.</returns>
    public static WorkflowError FromException(Exception ex, string? nodeId = null)
    {
        var (errorType, data) = ex is WorkflowUserException wue
            ? (wue.ErrorType, wue.Data as object)
            : (ex.GetType().Name, (object?)null);

        return new WorkflowError(
            ErrorType: errorType,
            Message: ex.Message,
            NodeId: nodeId,
            Data: data,
            OccurredAt: DateTimeOffset.UtcNow,
            StackTrace: ex.StackTrace);
    }
}

/// <summary>
/// 💥 Exception thrown by <c>builtin.throw</c> to represent a user-defined workflow error~
/// Carries a structured <see cref="ErrorType"/> classification and optional <see cref="Data"/>
/// payload, which are forwarded into the <c>WorkflowError</c> catch payload~ 🌸✨
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.2.4 — <c>ThrowModule</c> throws this exception.
/// <c>NodeExecutor</c> catches it via the normal <c>SendFailure</c> path, turning it into a
/// <c>NodeExecutionFailed</c> message. When inside a try/catch boundary,
/// <c>TryCatchExecutorActor</c> catches the <c>SubGraphFailed</c> and builds a
/// <see cref="WorkflowError"/> from it using <see cref="WorkflowError.FromException"/>~ 💖.
/// </remarks>
public sealed class WorkflowUserException : Exception
{
    /// <summary>Gets the classification string for this error (e.g. "ValidationError")~ 🏷️.</summary>
    public string ErrorType { get; }

    /// <summary>Gets optional structured data attached to the error~ 📦.</summary>
    public new object? Data { get; }

    /// <summary>
    /// Initializes a new <see cref="WorkflowUserException"/>~ 💥
    /// </summary>
    /// <param name="errorType">Error classification string.</param>
    /// <param name="message">Human-readable message.</param>
    /// <param name="data">Optional structured payload.</param>
    public WorkflowUserException(string errorType, string message, object? data = null)
        : base(message)
    {
        ErrorType = errorType;
        Data = data;
    }
}

