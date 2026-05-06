// <copyright file="ScopeMessages.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Messages;

using Workflow.Engine.Models;

/// <summary>
/// Sent to <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to push a new loop scope
/// onto the executor's loop context stack~ 🔁✨
/// </summary>
/// <param name="Context">The new loop context to push. 🌀.</param>
/// <remarks>
/// CopilotNote: Phase 2.2.0b infrastructure message. The LoopModule (2.2.2) sends this
/// to WorkflowExecutor before spawning a loop iteration sub-graph, and sends
/// <see cref="PopLoopScope"/> when the loop completes~ 💖.
/// </remarks>
public record PushLoopScope(LoopContext Context);

/// <summary>
/// Sent to <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to pop the active loop
/// scope off the stack~ ⬆️✨
/// </summary>
/// <param name="LoopId">The loop ID to pop (sanity check against top of stack). 🆔.</param>
public record PopLoopScope(string LoopId);

/// <summary>
/// Sent to <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to push an error boundary
/// onto the executor's boundary stack~ 🛡️✨
/// </summary>
/// <param name="Boundary">The error boundary to push. 🛡️.</param>
/// <remarks>
/// CopilotNote: Phase 2.2.0b infrastructure message. The TryCatch module (2.2.4) sends this
/// when the try-body starts, and sends <see cref="PopErrorBoundary"/> when it exits (success
/// or after catch/finally)~ 💖.
/// </remarks>
public record PushErrorBoundary(ErrorBoundary Boundary);

/// <summary>
/// Sent to <see cref="Workflow.Engine.Actors.WorkflowExecutor"/> to pop the active error
/// boundary off the stack~ ⬆️✨
/// </summary>
/// <param name="BoundaryId">The boundary ID to pop (sanity check against top of stack). 🆔.</param>
public record PopErrorBoundary(string BoundaryId);

