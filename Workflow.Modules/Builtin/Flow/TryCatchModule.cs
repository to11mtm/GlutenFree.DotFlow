// <copyright file="TryCatchModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Flow;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🛡️ Built-in error boundary module (<c>builtin.trycatch</c>)~
/// Wraps a sub-graph in an error containment zone: on failure it routes to
/// the <c>catch</c> branch with a <see cref="WorkflowError"/> payload, then
/// always routes to the <c>finally</c> branch (if connected) before continuing~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.4 — returns <see cref="ModuleResult.WithTryCatch"/> which
/// causes <c>WorkflowExecutor</c> to spawn a <c>TryCatchExecutorActor</c>. The actor
/// orchestrates: try → (failure? → catch?) → finally? → done continuation~ 🌸.
/// </para>
/// <para>
/// Schema ports summary:
/// <list type="bullet">
///   <item><c>try</c> — activation; connections form the try-body sub-graph</item>
///   <item><c>catch</c> — activation + <c>WorkflowError</c> payload; connections form the catch-body (optional)</item>
///   <item><c>finally</c> — activation; always runs after try+catch completes (optional)</item>
///   <item><c>done</c> — activation; fires after the whole sequence, for workflow continuation</item>
/// </list>
/// </para>
/// </remarks>
public class TryCatchModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.trycatch";

    /// <inheritdoc />
    public string DisplayName => "Try Catch";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Wraps a sub-graph in an error boundary — routes to catch on failure, always runs finally~ 🛡️✨";

    /// <inheritdoc />
    public string Icon => "🛡️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Outputs intentionally EMPTY (dynamic ports: try, catch, finally, done).
    /// ValidateConnectionPorts skips port-name validation for this module~ 🎗️
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "rethrow",
                DisplayName: "Rethrow",
                DataType: typeof(bool),
                Description: "When true, re-raises the caught error after finally executes~ ❗",
                IsRequired: false),
            new PortDefinition(
                Name: "catchTypes",
                DisplayName: "Catch Types",
                DataType: typeof(object),
                Description: "Array of error type names to catch (leave empty for catch-all)~ 🎣",
                IsRequired: false)),
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "rethrow",
                DisplayName: "Rethrow Error",
                DataType: typeof(bool),
                Description: "Re-raise the error after finally runs (default false)~ ❗",
                IsRequired: false,
                DefaultValue: false,
                EditorType: PropertyEditorType.Boolean),
            new ModulePropertyDefinition(
                Name: "catchTypes",
                DisplayName: "Catch Types",
                DataType: typeof(string),
                Description: "Comma-separated or JSON array of error type strings to catch~ 🎣",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json)));

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // ── Resolve rethrow ──────────────────────────────────────────────────────────────
        var rethrow = false;
        if (context.Inputs.TryGetValue("rethrow", out var rethrowIn) && rethrowIn is not null)
        {
            rethrow = CoerceBool(rethrowIn);
        }
        else if (context.Properties.TryGetValue("rethrow", out var rethrowProp) && rethrowProp is bool rethrowBool)
        {
            rethrow = rethrowBool;
        }

        // ── Resolve catchTypes ───────────────────────────────────────────────────────────
        string[]? catchTypes = null;
        var catchTypesRaw = context.Inputs.TryGetValue("catchTypes", out var ctIn) && ctIn is not null
            ? ctIn
            : (context.Properties.TryGetValue("catchTypes", out var ctProp) ? ctProp : null);

        if (catchTypesRaw is not null)
        {
            catchTypes = CoerceCatchTypes(catchTypesRaw);
        }

        var request = new TryCatchRequest
        {
            Rethrow = rethrow,
            CatchTypes = catchTypes,
            TryPort = "try",
            CatchPort = "catch",
            FinallyPort = "finally",
            DonePort = "done",
        };

        context.Logger.LogInformation(
            "🛡️ TryCatchModule: emitting TryCatchRequest (rethrow={Rethrow}, catchTypes={CatchTypes})",
            rethrow, catchTypes is null ? "any" : string.Join(",", catchTypes));

        return Task.FromResult(ModuleResult.WithTryCatch(new Dictionary<string, object?>(), request));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static bool CoerceBool(object value) => value switch
    {
        bool b => b,
        int i => i != 0,
        string s when s.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        string s when s.Equals("1", StringComparison.OrdinalIgnoreCase) => true,
        _ => false,
    };

    private static string[]? CoerceCatchTypes(object value)
    {
        switch (value)
        {
            case string[] arr:
                return arr.Length == 0 ? null : arr;

            case List<object?> list:
                var types = list
                    .OfType<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
                return types.Length == 0 ? null : types;

            case string s when s.TrimStart().StartsWith("[", StringComparison.Ordinal):
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(s);
                    return parsed is { Length: > 0 } ? parsed : null;
                }
                catch
                {
                    return null;
                }

            case string s when !string.IsNullOrWhiteSpace(s):
                // Comma-separated~
                var split = s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return split.Length == 0 ? null : split;

            default:
                return null;
        }
    }
}

