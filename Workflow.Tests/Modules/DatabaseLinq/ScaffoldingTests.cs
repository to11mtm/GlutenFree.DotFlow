// <copyright file="ScaffoldingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Database.Linq;
using Xunit;

/// <summary>
/// 🧬🏗️ Phase 2.4.b.0 — Scaffolding tests for the typed linq family project~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote (deviation): the plan's placeholder test name
/// <c>AddDatabaseLinqModules_RegistersCompilerPreviewerAndModule</c> can't be honest yet — the
/// compiler/previewer/module don't exist until 2.4.b.1/2.4.b.3/2.4.b.4. This slice instead verifies
/// the three things that ARE true at scaffold time: (1) the opt-in entry point is chainable +
/// idempotent, (2) the Roslyn toolchain resolves inside this assembly, and (3) — the important D14
/// guarantee — Roslyn stays quarantined out of <c>Workflow.Modules</c>~ 🌸.
/// </remarks>
public sealed class ScaffoldingTests
{
    [Fact]
    public void AddDatabaseLinqModules_IsChainableAndIdempotent()
    {
        var services = new ServiceCollection();

        var returned = services.AddDatabaseLinqModules();
        returned.Should().BeSameAs(services, "the extension returns the same collection for chaining~ 🔗");

        // Idempotent — calling again must not throw or duplicate-register anything problematic~
        var act = () => services.AddDatabaseLinqModules().AddDatabaseLinqModules();
        act.Should().NotThrow();

        // Exactly one IWorkflowModule (builtin.database.linq) is registered (2.4.b.3)~ 🌟
        using var provider = services.BuildServiceProvider();
        provider.GetServices<IWorkflowModule>().Should().ContainSingle(m => m.ModuleId == "builtin.database.linq");
    }

    [Fact]
    public void RoslynToolchain_ResolvesInsideLinqAssembly()
    {
        // Proves Microsoft.CodeAnalysis.CSharp + Basic.Reference.Assemblies restore + run~ 🧠
        AssemblyMarker.RoslynToolchainSmoke().Should().BeGreaterThan(0);
        AssemblyMarker.ReferenceAssembliesAssemblyName.Should().StartWith(
            "Basic.Reference.Assemblies", "the portable ref-assembly package is referenced~ 📚");
    }

    [Fact]
    public void LinqAssembly_ReferencesRoslynAndBasicRefs()
    {
        var referenced = typeof(AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenced.Should().Contain(n => n!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal),
            "the linq family owns the Roslyn dependency~ 🧬");
        referenced.Should().Contain(n => n!.StartsWith("Basic.Reference.Assemblies", StringComparison.Ordinal),
            "portable reference assemblies live here too~ 📚");
    }

    [Fact]
    public void WorkflowModules_DoesNotReferenceRoslyn_QuarantineHolds()
    {
        // D14: the minimal module path (AddWorkflowModules lives in Workflow.Modules) must NOT pull
        // Roslyn. Assert on the compile-time reference set — deterministic, unlike runtime load state~ 🔒
        var referenced = typeof(IWorkflowModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenced.Should().NotContain(n => n!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal),
            "AddWorkflowModules() / raw-SQL hosts must never transitively load Roslyn (D14)~ 🛡️");
    }
}


