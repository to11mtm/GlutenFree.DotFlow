// <copyright file="VariableScope.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Models;

/// <summary>
/// 🎯 The kind of scope a variable belongs to~ ✨
/// </summary>
public enum VariableScopeKind
{
    /// <summary>Variable is available across all workflows.</summary>
    Global,

    /// <summary>Variable is scoped to a specific workflow definition.</summary>
    Workflow,

    /// <summary>Variable is scoped to a single execution.</summary>
    Execution,
}

/// <summary>
/// 💾 Identifies the scope of a persisted variable. Variables are isolated by scope:
/// Global (shared), Workflow (per-definition), or Execution (per-run)~ 🔒
/// </summary>
/// <remarks>
/// CopilotNote: Use the static factory methods for clarity:
/// <c>VariableScope.Global</c>, <c>VariableScope.ForWorkflow(id)</c>,
/// <c>VariableScope.ForExecution(id)</c>~ UwU 💖
/// </remarks>
public record VariableScope(
    VariableScopeKind Kind,
    Guid? WorkflowId = null,
    Guid? ExecutionId = null)
{
    /// <summary>Gets the global variable scope (shared across all workflows)~ 🌍.</summary>
    public static VariableScope Global { get; } = new(VariableScopeKind.Global);

    /// <summary>Creates a workflow-scoped variable scope~ 📋.</summary>
    public static VariableScope ForWorkflow(Guid workflowId) =>
        new(VariableScopeKind.Workflow, WorkflowId: workflowId);

    /// <summary>Creates an execution-scoped variable scope~ ⚡.</summary>
    public static VariableScope ForExecution(Guid executionId) =>
        new(VariableScopeKind.Execution, ExecutionId: executionId);
}

