// <copyright file="SampleInvalidModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.SampleModules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// ❌ A sample module with an INVALID ModuleId, used to verify that the dynamic
/// loader correctly skips modules that fail <c>ModuleValidator</c> validation~ 🛡️.
/// </summary>
/// <remarks>
/// CopilotNote: The uppercase ID violates the naming convention regex
/// <c>^[a-z][a-z0-9._-]*$</c> and should cause the validator to reject it.
/// The loader must NOT crash — it should just skip this module with a warning~ 💖.
/// </remarks>
public sealed class SampleInvalidModule : IWorkflowModule
{
    // CopilotNote: INTENTIONALLY invalid ID! The validator should reject this~ 🎯
    /// <inheritdoc />
    public string ModuleId => "INVALID-SAMPLE-MODULE";

    /// <inheritdoc />
    public string DisplayName => "Sample Invalid Module";

    /// <inheritdoc />
    public string Category => "Samples";

    /// <inheritdoc />
    public string Description => "A sample module with an invalid ID for loader validation testing.";

    /// <inheritdoc />
    public string Icon => "❌";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}
