// <copyright file="IWorkflowModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;

/// <summary>
/// 🌸 The base interface all workflow modules must implement!
/// This is the contract for creating new modules~ UwU
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Module authors implement this interface to create
/// custom workflow nodes. Keep it simple and stateless for best results!
/// </para>
/// </remarks>
public interface IWorkflowModule
{
    /// <summary>
    /// Gets the unique identifier for this module type. ✨
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// Gets the display name shown in the UI. 🎨
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the category for organizing in the module palette. 📁
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Gets the description of what this module does. 📝
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the icon identifier (emoji or icon name). 🖼️
    /// </summary>
    string Icon { get; }

    /// <summary>
    /// Gets the input/output schema for this module. 📋
    /// Uses the unified ModuleSchema from Workflow.Core.Models.
    /// </summary>
    ModuleSchema Schema { get; }

    /// <summary>
    /// Execute the module's logic! This is where the magic happens~ ✨
    /// </summary>
    /// <param name="context">Execution context with inputs and services.</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown.</param>
    /// <returns>The execution result with outputs.</returns>
    Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 📦 Context provided to modules during execution.
/// </summary>
public record ModuleExecutionContext
{
    /// <summary>
    /// Gets the input values from connected ports. 📥
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }

    /// <summary>
    /// Gets the configured property values. ⚙️
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }

    /// <summary>
    /// Gets the workflow-level variables. 🔧
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }

    /// <summary>
    /// Gets the logger for this module execution. 📝
    /// </summary>
    public required ILogger Logger { get; init; }

    /// <summary>
    /// Gets the service provider for dependency injection. 💉
    /// </summary>
    public required IServiceProvider Services { get; init; }

    /// <summary>
    /// Gets the unique execution ID for tracing. 🔍
    /// </summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>
    /// Gets the node instance ID within the workflow. 🆔
    /// </summary>
    public required string NodeId { get; init; }
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
    /// Creates a successful result with outputs.
    /// </summary>
    /// <param name="outputs">The output values.</param>
    /// <returns>A successful ModuleResult.</returns>
    public static ModuleResult Ok(Dictionary<string, object?> outputs)
        => new() { Success = true, Outputs = outputs };

    /// <summary>
    /// Creates a failed result with error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="ex">Optional exception.</param>
    /// <returns>A failed ModuleResult.</returns>
    public static ModuleResult Fail(string message, Exception? ex = null)
        => new() { Success = false, ErrorMessage = message, Exception = ex };
}

