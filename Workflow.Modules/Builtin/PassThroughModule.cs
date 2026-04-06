// <copyright file="PassThroughModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🔄 A simple pass-through module that copies inputs to outputs.
/// Useful for testing data flow and as a template for new modules~ ✨
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the simplest possible module implementation.
/// It just passes all inputs through as outputs, making it perfect
/// for testing the workflow engine without any business logic.
/// </para>
/// </remarks>
public class PassThroughModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.passthrough";

    /// <inheritdoc />
    public string DisplayName => "Pass Through";

    /// <inheritdoc />
    public string Category => "Utilities";

    /// <inheritdoc />
    public string Description => "Passes all inputs through to outputs unchanged. Useful for debugging and testing.";

    /// <inheritdoc />
    public string Icon => "🔄";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "input",
                DisplayName: "Input",
                DataType: typeof(object),
                Description: "Any value to pass through",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition(
                Name: "output",
                DisplayName: "Output",
                DataType: typeof(object),
                Description: "The same value as input",
                IsRequired: false)),
        Properties: Arr<ModulePropertyDefinition>.Empty);

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        context.Logger.LogInformation("🔄 PassThrough module executing for node {NodeId}", context.NodeId);

        // Copy all inputs to outputs
        var outputs = new Dictionary<string, object?>();

        foreach (var (key, value) in context.Inputs)
        {
            outputs[key] = value;
            context.Logger.LogDebug("  📥 {Key} = {Value}", key, value);
        }

        context.Logger.LogInformation("✅ PassThrough module completed with {OutputCount} outputs", outputs.Count);

        return Task.FromResult(ModuleResult.Ok(outputs));
    }
}

