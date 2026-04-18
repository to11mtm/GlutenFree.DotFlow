// <copyright file="SampleDelayModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.SampleModules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// ⏱️ A sample delay module used for testing the dynamic module loader.
/// Introduces a configurable delay and then passes data through~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: This is the second sample module to verify that the loader
/// can discover MULTIPLE modules in the same assembly~ 💖
/// </remarks>
public sealed class SampleDelayModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "sample.delay";

    /// <inheritdoc />
    public string DisplayName => "Sample Delay";

    /// <inheritdoc />
    public string Category => "Samples";

    /// <inheritdoc />
    public string Description => "A sample delay module for dynamic loader tests.";

    /// <inheritdoc />
    public string Icon => "⏱️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "delayMs",
                DisplayName: "Delay (ms)",
                DataType: typeof(int),
                Description: "How long to delay in milliseconds.",
                IsRequired: false,
                DefaultValue: 0)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "elapsed",
                DisplayName: "Elapsed",
                DataType: typeof(long),
                Description: "Actual elapsed milliseconds.",
                IsRequired: false)),
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var delayMs = context.Inputs.TryGetValue("delayMs", out var raw) && raw is int d ? d : 0;
        context.Logger.LogDebug("⏱️ SampleDelayModule: waiting {DelayMs}ms~", delayMs);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        sw.Stop();

        return ModuleResult.Ok(new Dictionary<string, object?> { ["elapsed"] = sw.ElapsedMilliseconds });
    }
}

