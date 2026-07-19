// <copyright file="ModuleDependencyResolverTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Dependencies;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Dependencies;
using Xunit;

/// <summary>
/// 🔗 Phase 2.8.1 — Tests for <see cref="ModuleDependencyResolver"/>~ ✨.
/// </summary>
public sealed class ModuleDependencyResolverTests
{
    [Fact]
    public void Resolve_NoDeps_AnyOrder()
    {
        var resolver = new ModuleDependencyResolver();
        var modules = new[] { new StubModule("a"), new StubModule("b"), new StubModule("c") };

        var result = resolver.Resolve(modules);

        result.Success.Should().BeTrue();
        result.Ordered.Select(m => m.ModuleId).Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void Resolve_Chain_OrdersCorrectly()
    {
        var resolver = new ModuleDependencyResolver();
        var modules = new[]
        {
            new StubModule("c", dependencies: new[] { "b" }),
            new StubModule("b", dependencies: new[] { "a" }),
            new StubModule("a"),
        };

        var result = resolver.Resolve(modules);

        result.Success.Should().BeTrue();
        var order = result.Ordered.Select(m => m.ModuleId).ToList();
        order.IndexOf("a").Should().BeLessThan(order.IndexOf("b"));
        order.IndexOf("b").Should().BeLessThan(order.IndexOf("c"));
    }

    [Fact]
    public void Resolve_Diamond_OrdersCorrectly()
    {
        var resolver = new ModuleDependencyResolver();
        var modules = new[]
        {
            new StubModule("top", dependencies: new[] { "left", "right" }),
            new StubModule("left", dependencies: new[] { "bottom" }),
            new StubModule("right", dependencies: new[] { "bottom" }),
            new StubModule("bottom"),
        };

        var result = resolver.Resolve(modules);

        result.Success.Should().BeTrue();
        var order = result.Ordered.Select(m => m.ModuleId).ToList();
        order.IndexOf("bottom").Should().BeLessThan(order.IndexOf("left"));
        order.IndexOf("bottom").Should().BeLessThan(order.IndexOf("right"));
        order.IndexOf("left").Should().BeLessThan(order.IndexOf("top"));
        order.IndexOf("right").Should().BeLessThan(order.IndexOf("top"));
    }

    [Fact]
    public void Resolve_Cycle_ReportsCyclePath()
    {
        var resolver = new ModuleDependencyResolver();
        var modules = new[]
        {
            new StubModule("a", dependencies: new[] { "b" }),
            new StubModule("b", dependencies: new[] { "c" }),
            new StubModule("c", dependencies: new[] { "a" }),
        };

        var result = resolver.Resolve(modules);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("Circular").And.Contain("→");
    }

    [Fact]
    public void Resolve_MissingDep_ReportsMissingAndDependent()
    {
        var resolver = new ModuleDependencyResolver();
        var modules = new[] { new StubModule("a", dependencies: new[] { "ghost" }) };

        var result = resolver.Resolve(modules);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("a") && e.Contains("ghost"));
    }

    [Fact]
    public void Resolve_DependencyInExistingRegistry_Satisfied()
    {
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new StubModule("base"));
        var resolver = new ModuleDependencyResolver(registry);

        var result = resolver.Resolve(new[] { new StubModule("a", dependencies: new[] { "base" }) });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void GetDependents_ReverseLookup_Works()
    {
        var resolver = new ModuleDependencyResolver();
        var all = new[]
        {
            new StubModule("core"),
            new StubModule("a", dependencies: new[] { "core" }),
            new StubModule("b", dependencies: new[] { "core" }),
            new StubModule("c", dependencies: new[] { "a" }),
        };

        resolver.GetDependents("core", all).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void RegisterInDependencyOrder_RegistersAll()
    {
        var registry = new InMemoryModuleRegistry();
        var modules = new[]
        {
            new StubModule("c", dependencies: new[] { "b" }),
            new StubModule("b", dependencies: new[] { "a" }),
            new StubModule("a"),
        };

        var result = Workflow.Modules.Discovery.ModuleRegistryExtensions.RegisterInDependencyOrder(registry, modules);

        result.Success.Should().BeTrue();
        registry.HasModule("a").Should().BeTrue();
        registry.HasModule("b").Should().BeTrue();
        registry.HasModule("c").Should().BeTrue();
    }
}
