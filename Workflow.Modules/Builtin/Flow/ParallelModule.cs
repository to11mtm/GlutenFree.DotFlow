// <copyright file="ParallelModule.cs" company="GlutenFree">
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
/// 🌐 Built-in parallel fan-out module (<c>builtin.parallel</c>)~
/// Fans out execution to N concurrent branches (one sub-graph per output port),
/// then activates the <c>done</c> port when all branches complete~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.2.3a — output ports are DYNAMIC (one per branch + "done"),
/// so the schema declares an empty <c>Outputs</c> collection. <c>ValidateConnectionPorts</c>
/// skips port-name validation for this module — author-defined branch ports are trusted~ 🎗️
/// </para>
/// <para>
/// <b>Branches format</b> (property <c>branches</c>): either a JSON array of port-name
/// strings, OR omit it and set <c>branchCount</c> to auto-generate
/// <c>branch1, branch2, …</c> port names~
/// <code>
/// "branches": ["fetch_user", "fetch_orders", "fetch_metrics"]
/// </code>
/// </para>
/// <para>
/// Returns <see cref="ModuleResult.WithParallel"/> — the engine spawns
/// <c>ParallelExecutionCoordinator</c> to drive branch fan-out concurrently~ 🌐
/// </para>
/// </remarks>
public class ParallelModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.parallel";

    /// <inheritdoc />
    public string DisplayName => "Parallel";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Fans out execution to multiple concurrent branches and waits for all to complete~ 🌐✨";

    /// <inheritdoc />
    public string Icon => "🌐";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    /// <remarks>
    /// CopilotNote: Outputs intentionally EMPTY — branch ports are dynamic + "done" port.
    /// </remarks>
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr<PortDefinition>.Empty,
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "branches",
                DisplayName: "Branch Ports",
                DataType: typeof(string),
                Description: "JSON array of branch output port names. If omitted, uses branchCount~ 📋",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "branchCount",
                DisplayName: "Branch Count",
                DataType: typeof(int),
                Description: "Auto-generates branch1..branchN ports when 'branches' is omitted~ 🔢",
                IsRequired: false,
                DefaultValue: 2,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "maxDegreeOfParallelism",
                DisplayName: "Max Concurrency",
                DataType: typeof(int),
                Description: "Maximum branches running at once. 0 or negative = unbounded~ ⚡",
                IsRequired: false,
                DefaultValue: 0,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "failFast",
                DisplayName: "Fail Fast",
                DataType: typeof(bool),
                Description: "Cancel sibling branches on first failure (default true)~ 🛑",
                IsRequired: false,
                DefaultValue: true,
                EditorType: PropertyEditorType.Boolean),
            new ModulePropertyDefinition(
                Name: "waitForAll",
                DisplayName: "Wait For All",
                DataType: typeof(bool),
                Description: "When false, completes on first successful branch and cancels siblings~ 🏁",
                IsRequired: false,
                DefaultValue: true,
                EditorType: PropertyEditorType.Boolean),
            new ModulePropertyDefinition(
                Name: "donePort",
                DisplayName: "Done Port",
                DataType: typeof(string),
                Description: "Output port to fire when all branches complete (default 'done')~ ✅",
                IsRequired: false,
                DefaultValue: "done",
                EditorType: PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var branches = ResolveBranchPorts(configuration, out var error);
        if (error is not null)
        {
            return ValidationResult.Failure(
                new ValidationError("INVALID_BRANCHES", $"Cannot resolve branches: {error}~ 💔", "branches"));
        }

        if (branches is null || branches.Count == 0)
        {
            return ValidationResult.Failure(
                new ValidationError("EMPTY_BRANCHES", "At least one branch is required~ 💔", "branches"));
        }

        // Detect duplicates~
        var distinct = branches.Distinct(StringComparer.Ordinal).Count();
        if (distinct != branches.Count)
        {
            return ValidationResult.Failure(
                new ValidationError("DUPLICATE_BRANCHES", "Branch port names must be unique~ 💔", "branches"));
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var branches = ResolveBranchPorts(context.Properties, out var error);
        if (error is not null || branches is null || branches.Count == 0)
        {
            return Task.FromResult(ModuleResult.Fail(
                $"ParallelModule: invalid branches configuration: {error ?? "empty"}~ 💔"));
        }

        var maxDoP = int.MaxValue;
        if (context.Properties.TryGetValue("maxDegreeOfParallelism", out var dopRaw) && dopRaw is not null)
        {
            var parsed = ToInt(dopRaw);
            if (parsed > 0)
            {
                maxDoP = parsed;
            }
        }

        var failFast = true;
        if (context.Properties.TryGetValue("failFast", out var ffRaw) && ffRaw is bool ffBool)
        {
            failFast = ffBool;
        }

        var waitForAll = true;
        if (context.Properties.TryGetValue("waitForAll", out var wfaRaw) && wfaRaw is bool wfaBool)
        {
            waitForAll = wfaBool;
        }

        var donePort = "done";
        if (context.Properties.TryGetValue("donePort", out var dpRaw) && dpRaw is string dpStr && !string.IsNullOrWhiteSpace(dpStr))
        {
            donePort = dpStr.Trim();
        }

        var request = new ParallelRequest
        {
            BranchPorts = branches,
            MaxDegreeOfParallelism = maxDoP,
            FailFast = failFast,
            WaitForAll = waitForAll,
            DonePort = donePort,
        };

        context.Logger.LogInformation(
            "🌐 ParallelModule: emitting ParallelRequest with {Count} branches (maxDoP={MaxDoP}, failFast={FailFast}, waitForAll={WaitForAll})",
            branches.Count, maxDoP, failFast, waitForAll);

        return Task.FromResult(ModuleResult.WithParallel(new Dictionary<string, object?>(), request));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the branch port name list from the configuration~ 🔧
    /// Priority: <c>branches</c> JSON array → auto-generated <c>branch1..branchN</c> via <c>branchCount</c>~
    /// </summary>
    private static List<string>? ResolveBranchPorts(IReadOnlyDictionary<string, object?> config, out string? error)
    {
        error = null;

        if (config.TryGetValue("branches", out var raw) && raw is not null)
        {
            // Pre-parsed list (from ConvertJsonElement)~
            if (raw is List<object?> list)
            {
                var ports = new List<string>(list.Count);
                foreach (var item in list)
                {
                    if (item is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        ports.Add(s.Trim());
                    }
                    else
                    {
                        error = $"each branch entry must be a non-empty string (got {item?.GetType().Name ?? "null"})";
                        return null;
                    }
                }

                return ports;
            }

            if (raw is string json && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var arr = JsonSerializer.Deserialize<List<string>>(json);
                    if (arr is null)
                    {
                        error = "deserialized to null";
                        return null;
                    }

                    return arr.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
                }
                catch (JsonException ex)
                {
                    error = ex.Message;
                    return null;
                }
            }
        }

        // Fallback to branchCount~
        var count = 2;
        if (config.TryGetValue("branchCount", out var bcRaw) && bcRaw is not null)
        {
            count = ToInt(bcRaw);
        }

        if (count <= 0)
        {
            error = "branchCount must be > 0";
            return null;
        }

        var generated = new List<string>(count);
        for (var i = 1; i <= count; i++)
        {
            generated.Add($"branch{i}");
        }

        return generated;
    }

    private static int ToInt(object value)
    {
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var p) => p,
            _ => 0,
        };
    }
}

