// <copyright file="DelayModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// ⏱️ Built-in Delay module (<c>builtin.delay</c>) — pauses workflow execution
/// for a configurable duration. Useful for rate-limiting, waiting for external
/// systems, or testing timing-sensitive workflows~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: The delay respects <see cref="CancellationToken"/> so workflows
/// can be stopped cleanly. A safety cap (<c>maxDurationMs</c>) prevents runaway waits~ 🛡️.
/// </para>
/// </remarks>
public class DelayModule : IWorkflowModule
{
    /// <summary>
    /// Default maximum delay in milliseconds (5 minutes)~ 🛡️.
    /// </summary>
    private const long DefaultMaxDurationMs = 300_000;

    /// <inheritdoc />
    public string ModuleId => "builtin.delay";

    /// <inheritdoc />
    public string DisplayName => "Delay";

    /// <inheritdoc />
    public string Category => "Flow Control";

    /// <inheritdoc />
    public string Description => "Pauses workflow execution for a configurable duration~ ⏱️✨";

    /// <inheritdoc />
    public string Icon => "⏱️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "actualDurationMs",
                DisplayName: "Actual Duration (ms)",
                DataType: typeof(long),
                Description: "Real elapsed milliseconds~ ⏱️",
                IsRequired: false),
            new PortDefinition(
                Name: "wasCancelled",
                DisplayName: "Was Cancelled",
                DataType: typeof(bool),
                Description: "True if cancellation fired before delay completed~ 🛑",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Delay in milliseconds. Supports {{Variable.Name}} references~ ⏱️",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "maxDurationMs",
                DisplayName: "Max Duration (ms)",
                DataType: typeof(long),
                Description: "Safety cap in milliseconds (default 300000 = 5 min)~ 🛡️",
                IsRequired: false,
                DefaultValue: DefaultMaxDurationMs,
                EditorType: PropertyEditorType.Number)));

    /// <summary>
    /// Validates that <c>durationMs</c> is non-negative and within the safety cap~ ✅.
    /// </summary>
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        var duration = TryParseLong(configuration, "durationMs");
        var maxDuration = TryParseLong(configuration, "maxDurationMs") ?? DefaultMaxDurationMs;

        if (duration.HasValue && duration.Value < 0)
        {
            errors.Add(new ValidationError(
                "NEGATIVE_DURATION",
                "durationMs must be non-negative~ 💔",
                PropertyName: "durationMs"));
        }

        if (duration.HasValue && duration.Value > maxDuration)
        {
            errors.Add(new ValidationError(
                "DURATION_EXCEEDS_MAX",
                $"durationMs ({duration.Value}) exceeds maxDurationMs ({maxDuration})~ 💔",
                PropertyName: "durationMs"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // Parse duration from properties~ ⏱️
        var durationMs = TryParseLong(context.Properties, "durationMs") ?? 0;
        var maxDurationMs = TryParseLong(context.Properties, "maxDurationMs") ?? DefaultMaxDurationMs;

        // Fail-fast if duration exceeds safety cap~ 🛡️
        if (durationMs > maxDurationMs)
        {
            return ModuleResult.Fail(
                $"durationMs ({durationMs}) exceeds maxDurationMs ({maxDurationMs})~ 💔");
        }

        // Negative duration = immediate pass-through (not an error)~ 🏃
        if (durationMs < 0)
        {
            durationMs = 0;
        }

        var sw = Stopwatch.StartNew();
        var wasCancelled = false;

        try
        {
            if (durationMs > 0)
            {
                context.Logger.LogDebug("⏱️ Delaying for {DurationMs}ms...", durationMs);
                await Task.Delay((int)durationMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            context.Logger.LogDebug("🛑 Delay was cancelled after {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }

        sw.Stop();

        var outputs = new Dictionary<string, object?>
        {
            ["actualDurationMs"] = sw.ElapsedMilliseconds,
            ["wasCancelled"] = wasCancelled,
        };

        return ModuleResult.Ok(outputs);
    }

    /// <summary>
    /// Attempts to parse a long from a property value (handles string, int, long, double)~ 🔧.
    /// </summary>
    private static long? TryParseLong(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return null;
        }

        return val switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}
