// <copyright file="StubModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧪 Phase 2.8 — A configurable in-memory module for dependency/versioning tests~ ✨.
/// </summary>
public sealed class StubModule : IWorkflowModule
{
    public StubModule(string id, Version? version = null, IReadOnlyList<string>? dependencies = null, string category = "Testing")
    {
        this.ModuleId = id;
        this.Version = version ?? new Version(1, 0, 0);
        this.Dependencies = dependencies ?? Array.Empty<string>();
        this.Category = category;
    }

    public string ModuleId { get; }

    public string DisplayName => this.ModuleId;

    public string Category { get; }

    public string Description => $"Stub module {this.ModuleId}.";

    public string Icon => "🧪";

    public Version Version { get; }

    public ModuleSchema Schema => ModuleSchema.Empty;

    public IReadOnlyList<string> Dependencies { get; }

    public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}
