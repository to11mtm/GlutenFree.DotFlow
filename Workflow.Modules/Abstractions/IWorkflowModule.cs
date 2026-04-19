// <copyright file="IWorkflowModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;

/// <summary>
/// 🌸 The base interface all workflow modules must implement!
/// This is the contract for creating new modules~ UwU.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Module authors implement this interface to create
/// custom workflow nodes. Keep it simple and stateless for best results!.
/// </para>
/// <para>
/// Phase 1.4.1 additions: Version, ValidateConfiguration (default impl),
/// Dependencies (default impl). All have safe defaults so existing modules
/// don't break~ non-breaking is the way! 💖.
/// </para>
/// </remarks>
public interface IWorkflowModule
{
    /// <summary>
    /// Gets the unique identifier for this module type. ✨.
    /// </summary>
    public string ModuleId { get; }

    /// <summary>
    /// Gets the display name shown in the UI. 🎨.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the category for organizing in the module palette. 📁.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets the description of what this module does. 📝.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the icon identifier (emoji or icon name). 🖼️.
    /// </summary>
    public string Icon { get; }

    /// <summary>
    /// Gets the version of this module for compatibility tracking. 🏷️.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Used for side-by-side versioning (Phase 2.8) and
    /// schema compatibility checks. Every module should declare a version! ✨.
    /// </remarks>
    public Version Version { get; }

    /// <summary>
    /// Gets the input/output schema for this module. 📋
    /// Uses the unified ModuleSchema from Workflow.Core.Models.
    /// </summary>
    public ModuleSchema Schema { get; }

    /// <summary>
    /// Gets the list of module IDs this module depends on. 🔗.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This is a stub for future dependency resolution (Phase 2.8).
    /// Modules CAN declare deps now, but resolution logic comes later~ 💖
    /// Default returns empty — most modules have no deps!.
    /// </remarks>
    public IReadOnlyList<string> Dependencies => Array.Empty<string>();

    /// <summary>
    /// Validates the given configuration before execution. ✅
    /// Override this to add custom validation logic for your module's properties!.
    /// </summary>
    /// <param name="configuration">The configuration dictionary to validate.</param>
    /// <returns>A <see cref="ValidationResult"/> indicating if the config is valid.</returns>
    /// <remarks>
    /// CopilotNote: The default implementation returns success — modules only
    /// need to override this if they have specific config validation needs.
    /// Called by ModuleValidator (Phase 1.4.3) and optionally by NodeExecutor~ 🎯.
    /// </remarks>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
        => ValidationResult.Success();

    /// <summary>
    /// Execute the module's logic! This is where the magic happens~ ✨.
    /// </summary>
    /// <param name="context">Execution context with inputs and services.</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    /// <returns>The execution result with outputs.</returns>
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 📦 Context provided to modules during execution.
/// </summary>
public record ModuleExecutionContext
{
    /// <summary>
    /// Gets the input values from connected ports. 📥.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }

    /// <summary>
    /// Gets the configured property values. ⚙️.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }

    /// <summary>
    /// Gets the workflow-level variables. 🔧.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }

    /// <summary>
    /// Gets the logger for this module execution. 📝.
    /// </summary>
    public required ILogger Logger { get; init; }

    /// <summary>
    /// Gets the service provider for dependency injection. 💉.
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the unique execution ID for tracing. 🔍.
    /// </summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>
    /// Gets the node instance ID within the workflow. 🆔.
    /// </summary>
    public required string NodeId { get; init; }
}

/// <summary>
/// 📊 Metrics captured during module execution.
/// </summary>
/// <remarks>
/// CopilotNote: NodeExecutor auto-populates Duration via Stopwatch.
/// Modules can optionally set MemoryBytes and CustomMetrics themselves
/// for richer observability! Phase 4 monitoring feeds from these~ ✨.
/// </remarks>
public record ExecutionMetrics
{
    /// <summary>
    /// Gets how long the module took to execute. ⏱️.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets the optional memory usage in bytes during execution. 🧠.
    /// </summary>
    public long? MemoryBytes { get; init; }

    /// <summary>
    /// Gets an optional extensible bag of custom metrics. 📦.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Use this for module-specific metrics like
    /// "rows_processed", "api_calls_made", etc. Phase 4 dashboards
    /// will aggregate these per-module~ 💖.
    /// </remarks>
    public HashMap<string, object>? CustomMetrics { get; init; }

    /// <summary>
    /// Creates an <see cref="ExecutionMetrics"/> with just a duration. ⏱️.
    /// </summary>
    /// <param name="duration">The execution duration.</param>
    /// <returns>A new <see cref="ExecutionMetrics"/> instance.</returns>
    public static ExecutionMetrics FromDuration(TimeSpan duration)
        => new() { Duration = duration };
}

/// <summary>
/// 🎯 Result of module execution.
/// </summary>
public record ModuleResult
{
    /// <summary>
    /// Gets whether the execution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the output values.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Gets the error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the exception if failed.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the execution metrics (duration, memory, custom). 📊.
    /// </summary>
    /// <remarks>
    /// CopilotNote: This is auto-populated by NodeExecutor with at least Duration.
    /// Modules can also set their own metrics via the Ok overload~ ✨.
    /// </remarks>
    public ExecutionMetrics? Metrics { get; init; }

    /// <summary>
    /// Gets the workflow variable mutations requested by this module execution. 💾.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: Modules cannot mutate <see cref="ModuleExecutionContext.Variables"/> directly
    /// because it's read-only. Instead, modules return variable updates here and the engine
    /// (<c>NodeExecutor</c> → <c>WorkflowExecutor</c>) applies them to the execution context
    /// so downstream nodes see the updated values~ 🔄.
    /// </para>
    /// <para>
    /// This is the mechanism that powers <c>SetVariableModule</c> (builtin.setvariable).
    /// Any module can use it — just return a dictionary of name→value pairs to merge! 💖.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? VariableUpdates { get; init; }

    /// <summary>
    /// Creates a successful result with outputs.
    /// </summary>
    /// <param name="outputs">The output values.</param>
    /// <returns>A successful ModuleResult.</returns>
    public static ModuleResult Ok(Dictionary<string, object?> outputs)
        => new() { Success = true, Outputs = outputs };

    /// <summary>
    /// Creates a successful result with outputs and execution metrics. 📊.
    /// </summary>
    /// <param name="outputs">The output values.</param>
    /// <param name="metrics">Execution metrics (duration, memory, custom).</param>
    /// <returns>A successful ModuleResult with metrics.</returns>
    public static ModuleResult Ok(Dictionary<string, object?> outputs, ExecutionMetrics metrics)
        => new() { Success = true, Outputs = outputs, Metrics = metrics };

    /// <summary>
    /// Creates a successful result with outputs and variable updates. 💾.
    /// </summary>
    /// <param name="outputs">The output values.</param>
    /// <param name="variableUpdates">Workflow variable mutations to apply after this node completes.</param>
    /// <returns>A successful ModuleResult with variable updates.</returns>
    public static ModuleResult Ok(
        Dictionary<string, object?> outputs,
        Dictionary<string, object?> variableUpdates)
        => new() { Success = true, Outputs = outputs, VariableUpdates = variableUpdates };

    /// <summary>
    /// Creates a successful result with outputs, metrics, and variable updates. 📊💾.
    /// </summary>
    /// <param name="outputs">The output values.</param>
    /// <param name="metrics">Execution metrics (duration, memory, custom).</param>
    /// <param name="variableUpdates">Workflow variable mutations to apply after this node completes.</param>
    /// <returns>A successful ModuleResult with metrics and variable updates.</returns>
    public static ModuleResult Ok(
        Dictionary<string, object?> outputs,
        ExecutionMetrics metrics,
        Dictionary<string, object?> variableUpdates)
        => new() { Success = true, Outputs = outputs, Metrics = metrics, VariableUpdates = variableUpdates };

    /// <summary>
    /// Creates a failed result with error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="ex">Optional exception.</param>
    /// <returns>A failed ModuleResult.</returns>
    public static ModuleResult Fail(string message, Exception? ex = null)
        => new() { Success = false, ErrorMessage = message, Exception = ex };
}
