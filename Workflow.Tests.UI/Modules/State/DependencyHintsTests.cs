// <copyright file="DependencyHintsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.State;

using System.Collections.Generic;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.State;
using Xunit;

/// <summary>
/// 🔗 Phase 3.6.3 — Tests for the framework-free <see cref="DependencyHints"/>~ ✨.
/// </summary>
public sealed class DependencyHintsTests
{
    private static ModuleDetailsDto Mod(string id, params string[] deps)
        => new(id, id, "cat", "d", "🔧", "1.0.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>(deps), true, new List<string> { "1.0.0" });

    [Fact]
    public void Deps_Dependents_Listed_SortedDistinct()
    {
        var known = new[]
        {
            Mod("a", "core"),
            Mod("b", "core", "a"),
            Mod("core"),
            Mod("c"),
        };

        DependencyHints.Dependents(known, "core").Should().ContainInOrder("a", "b");
        DependencyHints.Dependents(known, "a").Should().ContainSingle(d => d == "b");
    }

    [Fact]
    public void Deps_None_Empty()
        => DependencyHints.Dependents(new[] { Mod("a"), Mod("b") }, "a").Should().BeEmpty();

    [Fact]
    public void Deps_ExcludesSelf()
        => DependencyHints.Dependents(new[] { Mod("a", "a") }, "a").Should().BeEmpty();
}
