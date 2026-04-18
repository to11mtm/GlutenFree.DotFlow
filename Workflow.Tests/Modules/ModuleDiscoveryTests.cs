// <copyright file="ModuleDiscoveryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Discovery;
using Xunit;

/// <summary>
/// 🔍 Phase 1.4.5 — Tests for the Module Discovery system!
/// Covers assembly scanning, attribute-based ignoring, DI instantiation,
/// validation integration, duplicate handling, and metadata overrides~ ✨💖
/// </summary>
public class ModuleDiscoveryTests
{
    #region DiscoverModuleTypes Tests 🔎

    /// <summary>
    /// DiscoverModuleTypes should find public IWorkflowModule implementations in an assembly~ 📋
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_ShouldFindPublicModulesInAssembly()
    {
        // Arrange — scan Workflow.Modules, which has PassThroughModule~ ✨
        var discovery = new ModuleDiscovery();
        var assembly = typeof(PassThroughModule).Assembly;

        // Act
        var types = discovery.DiscoverModuleTypes(assembly);

        // Assert
        types.Should().NotBeEmpty("PassThroughModule is public and concrete~ UwU");
        types.Should().Contain(typeof(PassThroughModule));
    }

    /// <summary>
    /// DiscoverModuleTypes should skip abstract classes — can't instantiate those!~ 🚫
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_ShouldSkipAbstractClasses()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act
        var types = discovery.DiscoverModuleTypes(assembly);

        // Assert
        types.Should().NotContain(typeof(AbstractTestModule),
            "abstract classes cannot be instantiated and should be skipped~ 💖");
    }

    /// <summary>
    /// DiscoverModuleTypes should skip internal (non-public) classes~ 🔒
    /// GetExportedTypes() only returns public types, so internal classes are filtered out!
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_ShouldSkipNonPublicClasses()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act
        var types = discovery.DiscoverModuleTypes(assembly);

        // Assert
        types.Should().NotContain(typeof(InternalTestModule),
            "internal classes are not exported and should be excluded~ 💖");
    }

    /// <summary>
    /// DiscoverModuleTypes should respect [WorkflowModule(Ignore = true)]~ 🚫
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_ShouldRespectIgnoreAttribute()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act
        var types = discovery.DiscoverModuleTypes(assembly);

        // Assert
        types.Should().NotContain(typeof(IgnoredTestModule),
            "modules marked with [WorkflowModule(Ignore = true)] should be excluded~ UwU");
    }

    /// <summary>
    /// DiscoverModuleTypes on an assembly with no modules returns an empty list~ 📭
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_EmptyAssembly_ShouldReturnEmpty()
    {
        // Arrange — use a system assembly that has no IWorkflowModule implementations~ 🎯
        var discovery = new ModuleDiscovery();
        var assembly = typeof(string).Assembly; // mscorlib / System.Private.CoreLib

        // Act
        var types = discovery.DiscoverModuleTypes(assembly);

        // Assert
        types.Should().BeEmpty("core runtime assembly has no workflow modules~ 💖");
    }

    /// <summary>
    /// DiscoverModuleTypes should throw ArgumentNullException for null assembly~ 🛑
    /// </summary>
    [Fact]
    public void DiscoverModuleTypes_NullAssembly_ShouldThrow()
    {
        // Arrange
        var discovery = new ModuleDiscovery();

        // Act & Assert
        var act = () => discovery.DiscoverModuleTypes(null!);
        act.Should().Throw<ArgumentNullException>("null assembly is not allowed~ UwU");
    }

    #endregion

    #region DiscoverAndRegister Tests 🚀

    /// <summary>
    /// DiscoverAndRegister should scan the Workflow.Modules assembly and populate the registry~ ✨
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_ShouldPopulateRegistry()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(PassThroughModule).Assembly;

        // Act
        var count = discovery.DiscoverAndRegister(assembly, registry);

        // Assert
        count.Should().BeGreaterThan(0, "at least PassThroughModule should be discovered~ 💖");
        registry.HasModule("builtin.passthrough").Should().BeTrue(
            "PassThroughModule should be registered by discovery~ ✨");
    }

    /// <summary>
    /// DiscoverAndRegister should return 0 when no modules exist in the assembly~ 📭
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_EmptyAssembly_ShouldReturnZero()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(string).Assembly;

        // Act
        var count = discovery.DiscoverAndRegister(assembly, registry);

        // Assert
        count.Should().Be(0, "core runtime assembly has no workflow modules~ UwU");
    }

    /// <summary>
    /// DiscoverAndRegister should skip invalid modules and not throw~ 🛡️
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_ShouldSkipInvalidModules()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act — should NOT throw even though InvalidDiscoveryTestModule has a bad ID!
        var act = () => discovery.DiscoverAndRegister(assembly, registry);

        // Assert
        act.Should().NotThrow("discovery is resilient — bad modules are skipped, not crash~ 💖");
        registry.HasModule("INVALID-MODULE-ID").Should().BeFalse(
            "module with invalid ID should be rejected by validator~ ✨");
    }

    /// <summary>
    /// DiscoverAndRegister should handle duplicate module IDs gracefully~ 🔄
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_ShouldHandleDuplicatesGracefully()
    {
        // Arrange — pre-register PassThroughModule, then discover same assembly again
        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(PassThroughModule).Assembly;
        registry.RegisterModule(new PassThroughModule());

        // Act — should NOT throw even though PassThroughModule is already registered!
        var act = () => discovery.DiscoverAndRegister(assembly, registry);

        // Assert
        act.Should().NotThrow("duplicate modules are warned and skipped, not crashed~ UwU");
    }

    /// <summary>
    /// DiscoverAndRegister should use DI for constructor injection when services provided~ 💉
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_WithServiceProvider_ShouldUseDI()
    {
        // Arrange — set up DI with a required service
        var services = new ServiceCollection();
        services.AddSingleton<IDiscoveryTestDependency, DiscoveryTestDependency>();
        var serviceProvider = services.BuildServiceProvider();

        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act
        discovery.DiscoverAndRegister(assembly, registry, serviceProvider);

        // Assert — the DI module should have been registered successfully~ ✨
        registry.HasModule("test.discovery.dimodule").Should().BeTrue(
            "DiRequiringTestModule needs IDiscoveryTestDependency injected via DI~ 💖");
    }

    /// <summary>
    /// DiscoverAndRegister should throw ArgumentNullException for null registry~ 🛑
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_NullRegistry_ShouldThrow()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var assembly = typeof(PassThroughModule).Assembly;

        // Act & Assert
        var act = () => discovery.DiscoverAndRegister(assembly, null!);
        act.Should().Throw<ArgumentNullException>("null registry is not allowed~ UwU");
    }

    #endregion

    #region Attribute Override Tests 🏷️

    /// <summary>
    /// When [WorkflowModule] has ModuleId/Category/Description overrides, they should
    /// be applied during discovery registration~ ✨
    /// </summary>
    [Fact]
    public void DiscoverAndRegister_ShouldApplyAttributeMetadataOverrides()
    {
        // Arrange
        var discovery = new ModuleDiscovery();
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(ModuleDiscoveryTests).Assembly;

        // Act
        discovery.DiscoverAndRegister(assembly, registry);

        // Assert — the overridden ID from the attribute should be used~ 🏷️
        registry.HasModule("test.discovery.overridden").Should().BeTrue(
            "attribute override should replace the module's own ID~ UwU");

        var module = registry.GetModule("test.discovery.overridden");
        module.Should().NotBeNull();
        module!.Category.Should().Be("OverriddenCategory",
            "attribute Category override should be applied~ 💖");
        module.Description.Should().Be("Overridden description via attribute.",
            "attribute Description override should be applied~ ✨");
    }

    #endregion

    #region Extension Method Tests 🎀

    /// <summary>
    /// DiscoverAndRegisterFrom extension should scan the given assembly~ ✨
    /// </summary>
    [Fact]
    public void DiscoverAndRegisterFrom_ShouldRegisterModulesFromAssembly()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var assembly = typeof(PassThroughModule).Assembly;

        // Act
        var count = registry.DiscoverAndRegisterFrom(assembly);

        // Assert
        count.Should().BeGreaterThan(0, "at least one module exists in Workflow.Modules~ 💖");
        registry.HasModule("builtin.passthrough").Should().BeTrue();
    }

    /// <summary>
    /// DiscoverAndRegisterFromCallingAssembly extension should scan this test assembly~ ✨
    /// </summary>
    [Fact]
    public void DiscoverAndRegisterFromCallingAssembly_ShouldRegisterFromTestAssembly()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();

        // Act — discovers from this very test assembly! 🎯
        var count = registry.DiscoverAndRegisterFromCallingAssembly();

        // Assert — ValidDiscoveryTestModule is public in this assembly~ 🌸
        count.Should().BeGreaterThan(0,
            "this test assembly has public IWorkflowModule implementations~ UwU");
        registry.HasModule("test.discovery.valid").Should().BeTrue(
            "ValidDiscoveryTestModule should be found in the calling assembly~ 💖");
    }

    #endregion
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 🧪 Test helper types at namespace level — needed for proper assembly export visibility~ 💖
// These must be PUBLIC so GetExportedTypes() can find (or skip) them correctly in tests!
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

/// <summary>
/// 🛑 Abstract module — should be SKIPPED by discovery (can't instantiate!)~ 🚫
/// </summary>
public abstract class AbstractTestModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "test.discovery.abstract";

    /// <inheritdoc />
    public string DisplayName => "Abstract Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "An abstract module that should be skipped during discovery.";

    /// <inheritdoc />
    public string Icon => "🛑";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public abstract Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 🚫 Module marked with Ignore = true — should be SKIPPED by discovery~ ❌
/// </summary>
[WorkflowModule(Ignore = true)]
public sealed class IgnoredTestModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "test.discovery.ignored";

    /// <inheritdoc />
    public string DisplayName => "Ignored Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "This module has Ignore=true and should never be discovered.";

    /// <inheritdoc />
    public string Icon => "🚫";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}

/// <summary>
/// ✅ A valid, public, concrete module for testing discovery~ 💖
/// </summary>
public sealed class ValidDiscoveryTestModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "test.discovery.valid";

    /// <inheritdoc />
    public string DisplayName => "Valid Discovery Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "A simple valid module for discovery tests.";

    /// <inheritdoc />
    public string Icon => "✅";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}

/// <summary>
/// ❌ A module with an invalid ModuleId (uppercase) — should be skipped by validator~ 💔
/// </summary>
public sealed class InvalidDiscoveryTestModule : IWorkflowModule
{
    // CopilotNote: Invalid ID (uppercase) intentionally so validator rejects it~ 🎯
    /// <inheritdoc />
    public string ModuleId => "INVALID-MODULE-ID";

    /// <inheritdoc />
    public string DisplayName => "Invalid Discovery Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "A module with an invalid ID for testing validator rejection in discovery.";

    /// <inheritdoc />
    public string Icon => "❌";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}

/// <summary>
/// 🏷️ A module with attribute metadata overrides — tests the override system~ ✨
/// The actual class ModuleId is "test.discovery.original" but the attribute overrides it
/// to "test.discovery.overridden", with Category and Description overrides too~
/// </summary>
[WorkflowModule(
    ModuleId = "test.discovery.overridden",
    Category = "OverriddenCategory",
    Description = "Overridden description via attribute.")]
public sealed class AttributeOverrideTestModule : IWorkflowModule
{
    // CopilotNote: These values should be replaced by the WorkflowModuleAttribute above~ 🏷️
    /// <inheritdoc />
    public string ModuleId => "test.discovery.original";

    /// <inheritdoc />
    public string DisplayName => "Attribute Override Test Module";

    /// <inheritdoc />
    public string Category => "OriginalCategory";

    /// <inheritdoc />
    public string Description => "Original description — should be overridden by the attribute.";

    /// <inheritdoc />
    public string Icon => "🏷️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
}

/// <summary>
/// 💉 Contract for the DI dependency needed by DiRequiringTestModule~ 🌸
/// </summary>
public interface IDiscoveryTestDependency
{
    /// <summary>Gets a test value from the dependency.</summary>
    public string GetTestValue();
}

/// <summary>
/// 💉 Concrete implementation of IDiscoveryTestDependency for DI tests~ ✨
/// </summary>
public sealed class DiscoveryTestDependency : IDiscoveryTestDependency
{
    /// <inheritdoc />
    public string GetTestValue() => "injected!";
}

/// <summary>
/// 💉 A module that requires constructor injection via DI~ 🏗️
/// </summary>
public sealed class DiRequiringTestModule : IWorkflowModule
{
    private readonly IDiscoveryTestDependency _dependency;

    /// <summary>
    /// Initializes a new instance of <see cref="DiRequiringTestModule"/> via DI~ 💉
    /// </summary>
    /// <param name="dependency">The injected test dependency.</param>
    public DiRequiringTestModule(IDiscoveryTestDependency dependency)
    {
        _dependency = dependency;
    }

    /// <inheritdoc />
    public string ModuleId => "test.discovery.dimodule";

    /// <inheritdoc />
    public string DisplayName => "DI Requiring Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "A module that requires IDiscoveryTestDependency via DI constructor injection.";

    /// <inheritdoc />
    public string Icon => "💉";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => ModuleSchema.Empty;

    /// <inheritdoc />
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var val = _dependency.GetTestValue();
        return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["result"] = val }));
    }
}

/// <summary>
/// 🔒 Internal module — should be SKIPPED by discovery (not exported)~ 🚫
/// CopilotNote: Internal access means GetExportedTypes() won't return this type~ 💖
/// </summary>
internal sealed class InternalTestModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "test.discovery.internal";

    /// <inheritdoc />
    public string DisplayName => "Internal Test Module";

    /// <inheritdoc />
    public string Category => "Testing";

    /// <inheritdoc />
    public string Description => "An internal module that should be skipped during discovery.";

    /// <inheritdoc />
    public string Icon => "🔒";

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

