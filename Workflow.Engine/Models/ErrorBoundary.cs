// <copyright file="ErrorBoundary.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Models;

using System;
using System.Linq;

/// <summary>
/// ️ Describes an error containment zone scoped to a region of the workflow~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.0b — ErrorBoundary is an immutable record pushed onto
/// WorkflowExecutor's <c>_boundaryStack</c> when the engine enters a try/catch region
/// (typically by a TryCatch module in 2.2.4 sending a <see cref="Workflow.Engine.Messages.PushErrorBoundary"/> message).
/// </para>
/// <para>
/// When a node fails in <c>HandleNodeFailure</c>, the WorkflowExecutor walks the boundary
/// stack looking for the innermost boundary that <see cref="Catches"/> the exception.
/// If found, the workflow routes to <see cref="CatchEntryNodeId"/> instead of failing~ .
/// </para>
/// <para>
/// The <see cref="FinallyEntryNodeId"/> is the node that executes regardless of whether the
/// try-body succeeded or failed. For 2.2.0b, the infrastructure is in place; the TryCatch
/// module (2.2.4) will use it to implement "finally always runs"~ .
/// </para>
/// </remarks>
/// <param name="BoundaryId">Unique ID for this boundary (typically the TryCatch node's ID)~ .</param>
/// <param name="CatchEntryNodeId">The node to route to when an exception is caught~ .</param>
/// <param name="FinallyEntryNodeId">
/// Optional node that always runs after the try or catch body completes~ .
/// Managed by the TryCatch module (2.2.4); wired here for infrastructure completeness.
/// </param>
/// <param name="CatchTypes">
/// Exception types this boundary catches.
/// <see langword="null"/> or empty means catch-all (catches any exception)~ ️.
/// </param>
public sealed record ErrorBoundary(
    string BoundaryId,
    string? CatchEntryNodeId,
    string? FinallyEntryNodeId = null,
    Type[]? CatchTypes = null)
{
    /// <summary>
    /// Returns <see langword="true"/> if this boundary handles the given exception~ ⚡.
    /// <br/>Returns <see langword="true"/> unconditionally when <see cref="CatchTypes"/> is null or empty.
    /// </summary>
    /// <param name="ex">The exception to test.</param>
    /// <returns><see langword="true"/> if this boundary should catch <paramref name="ex"/>.</returns>
    public bool Catches(Exception ex)
    {
        if (CatchTypes is not { Length: > 0 }) return true;
        return CatchTypes.Any(t => t.IsInstanceOfType(ex));
    }
}
