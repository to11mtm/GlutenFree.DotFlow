// <copyright file="ThrowModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 💥 Built-in error throw module (<c>builtin.throw</c>)~
/// Throws a structured <see cref="WorkflowUserException"/> that becomes a
/// <see cref="WorkflowError"/> when caught by an enclosing <c>builtin.trycatch</c> node~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.4 — always throws, never returns success. The
/// <c>NodeExecutor</c> catches the exception and emits <c>NodeExecutionFailed</c>.
/// When inside a <c>TryCatchExecutorActor</c> sub-graph, the <c>SubGraphFailed</c>
/// is received and a <see cref="WorkflowError"/> is built via
/// <c>WorkflowError.FromException</c>~ 🌸.
/// </para>
/// <para>
/// Schema inputs: <c>errorType</c> (string, required), <c>message</c> (string, required),
/// <c>data</c> (object, optional). All three can also be supplied via properties~ 💖.
/// </para>
/// </remarks>
public class ThrowModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.throw";

    /// <inheritdoc />
    public string DisplayName => "Throw Error";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Throws a structured WorkflowUserError, breaking the current node execution~ 💥✨";

    /// <inheritdoc />
    public string Icon => "💥";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "errorType",
                DisplayName: "Error Type",
                DataType: typeof(string),
                Description: "Classification string for the error (e.g. 'ValidationError')~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "message",
                DisplayName: "Message",
                DataType: typeof(string),
                Description: "Human-readable error message~ 💬",
                IsRequired: false),
            new PortDefinition(
                Name: "data",
                DisplayName: "Data",
                DataType: typeof(object),
                Description: "Optional structured payload attached to the error~ 📦",
                IsRequired: false)),
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "errorType",
                DisplayName: "Error Type",
                DataType: typeof(string),
                Description: "Classification string for the error~ 🏷️",
                IsRequired: false,
                DefaultValue: "WorkflowError",
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "message",
                DisplayName: "Error Message",
                DataType: typeof(string),
                Description: "Human-readable error message~ 💬",
                IsRequired: false,
                DefaultValue: "An error occurred",
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "data",
                DisplayName: "Data",
                DataType: typeof(string),
                Description: "Optional JSON payload attached to the error~ 📦",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        // errorType is optional (defaults to "WorkflowError") — nothing required at config time~
        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // ── Resolve errorType ──────────────────────────────────────────────────────────────
        var errorType = ResolveString(context, "errorType") ?? "WorkflowError";

        // ── Resolve message ────────────────────────────────────────────────────────────────
        var message = ResolveString(context, "message") ?? "An error occurred in the workflow";

        // ── Resolve data ───────────────────────────────────────────────────────────────────
        object? data = null;
        if (context.Inputs.TryGetValue("data", out var dataIn) && dataIn is not null)
        {
            data = dataIn;
        }
        else if (context.Properties.TryGetValue("data", out var dataProp) && dataProp is not null)
        {
            data = dataProp;
        }

        context.Logger.LogInformation(
            "💥 ThrowModule: throwing WorkflowUserException (errorType='{ErrorType}', message='{Message}')",
            errorType, message);

        // CopilotNote: throw directly — NodeExecutor catches all exceptions and calls SendFailure.
        // This becomes NodeExecutionFailed, caught by TryCatchExecutorActor or WorkflowExecutor~ 💥
        throw new WorkflowUserException(errorType, message, data);
    }

    private static string? ResolveString(ModuleExecutionContext context, string key)
    {
        if (context.Inputs.TryGetValue(key, out var inputVal) && inputVal is string s1 && !string.IsNullOrEmpty(s1))
            return s1;
        if (context.Properties.TryGetValue(key, out var propVal) && propVal is string s2 && !string.IsNullOrEmpty(s2))
            return s2;
        return null;
    }
}

