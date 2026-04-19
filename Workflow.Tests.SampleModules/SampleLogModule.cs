// <copyright file="SampleLogModule.cs" company="GlutenFree">
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
/// 📋 A sample logging module used for testing the dynamic module loader.
/// Simulates a real plugin module that would be loaded from disk at runtime~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: This module is ONLY used in loader integration tests to verify
/// that AssemblyModuleLoader can discover, load, and unload modules from a
/// separately built assembly. Never use this in production! 🧪.
/// </remarks>
public sealed class SampleLogModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "sample.log";

    /// <inheritdoc />
    public string DisplayName => "Sample Log";

    /// <inheritdoc />
    public string Category => "Samples";

    /// <inheritdoc />
    public string Description => "A sample logging module for dynamic loader tests.";

    /// <inheritdoc />
    public string Icon => "📋";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "message",
                DisplayName: "Message",
                DataType: typeof(string),
                Description: "The message to log.",
                IsRequired: true)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "logged",
                DisplayName: "Logged",
                DataType: typeof(bool),
                Description: "True if the message was logged successfully.",
                IsRequired: false)),
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var message = context.Inputs.TryGetValue("message", out var msg) ? msg?.ToString() : "(no message)";
        context.Logger.LogInformation("📋 SampleLogModule: {Message}", message);

        return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["logged"] = true }));
    }
}
