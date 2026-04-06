// <copyright file="ModuleRegistryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// 🗂️ Phase 1.4.2 — Tests for enhanced IModuleRegistry &amp; InMemoryModuleRegistry!
/// Covers category lookup, search, type-based registration, observer notifications,
/// duplicate registration policy, and subscription disposal. UwU ✨💖
/// </summary>
public class ModuleRegistryTests
{
    #region GetModulesByCategory Tests 📁

    /// <summary>
    /// GetModulesByCategory should return modules matching the given category~ 📁
    /// </summary>
    [Fact]
    public void GetModulesByCategory_ShouldReturnMatchingModules()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());    // Category: "Utilities"
        registry.RegisterModule(new TestLogicModule());       // Category: "Logic"
        registry.RegisterModule(new TestUtilityModule());     // Category: "Utilities"

        // Act
        var utilities = registry.GetModulesByCategory("Utilities");

        // Assert
        utilities.Should().HaveCount(2, "two modules are in the Utilities category~ uwu");
        utilities.Should().Contain(m => m.ModuleId == "builtin.passthrough");
        utilities.Should().Contain(m => m.ModuleId == "test.utility");
    }

    /// <summary>
    /// GetModulesByCategory should return empty for an unknown category~ 🤷
    /// </summary>
    [Fact]
    public void GetModulesByCategory_UnknownCategory_ShouldReturnEmpty()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());

        // Act
        var result = registry.GetModulesByCategory("NonExistentCategory");

        // Assert
        result.Should().BeEmpty("no modules exist in that category~ uwu");
    }

    /// <summary>
    /// GetModulesByCategory should be case-insensitive~ 🔤
    /// </summary>
    [Fact]
    public void GetModulesByCategory_ShouldBeCaseInsensitive()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule()); // Category: "Utilities"

        // Act
        var result = registry.GetModulesByCategory("utilities"); // lowercase

        // Assert
        result.Should().HaveCount(1, "category lookup should be case-insensitive~ uwu");
        result[0].ModuleId.Should().Be("builtin.passthrough");
    }

    #endregion

    #region SearchModules Tests 🔎

    /// <summary>
    /// SearchModules should find modules by ModuleId~ 🔎
    /// </summary>
    [Fact]
    public void SearchModules_ShouldFindByModuleId()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());
        registry.RegisterModule(new TestLogicModule());

        // Act
        var results = registry.SearchModules("passthrough");

        // Assert
        results.Should().ContainSingle(m => m.ModuleId == "builtin.passthrough");
    }

    /// <summary>
    /// SearchModules should find modules by DisplayName~ 🏷️
    /// </summary>
    [Fact]
    public void SearchModules_ShouldFindByDisplayName()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule()); // DisplayName: "Pass Through"
        registry.RegisterModule(new TestLogicModule());   // DisplayName: "Logic Gate"

        // Act
        var results = registry.SearchModules("Logic Gate");

        // Assert
        results.Should().ContainSingle(m => m.ModuleId == "test.logic");
    }

    /// <summary>
    /// SearchModules should find modules by Description~ 📝
    /// </summary>
    [Fact]
    public void SearchModules_ShouldFindByDescription()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule()); // Description contains "debugging"
        registry.RegisterModule(new TestLogicModule());

        // Act
        var results = registry.SearchModules("debugging");

        // Assert
        results.Should().ContainSingle(m => m.ModuleId == "builtin.passthrough");
    }

    /// <summary>
    /// SearchModules should return empty when nothing matches~ 🤷
    /// </summary>
    [Fact]
    public void SearchModules_NoMatch_ShouldReturnEmpty()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());

        // Act
        var results = registry.SearchModules("nonexistent-module-xyz");

        // Assert
        results.Should().BeEmpty("nothing matches the search query~ uwu");
    }

    #endregion

    #region RegisterModule(Type) Tests 🏭

    /// <summary>
    /// RegisterModule(Type) should create an instance and register it~ 🏭
    /// </summary>
    [Fact]
    public void RegisterModuleByType_ShouldCreateAndRegister()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();

        // Act
        registry.RegisterModule(typeof(PassThroughModule));

        // Assert
        registry.HasModule("builtin.passthrough").Should().BeTrue("type-based registration should work! uwu");
        registry.GetModule("builtin.passthrough").Should().NotBeNull();
    }

    /// <summary>
    /// RegisterModule(Type, IServiceProvider) should use DI for constructor injection~ 💉
    /// </summary>
    [Fact]
    public void RegisterModuleByType_WithServiceProvider_ShouldUseDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());
        var serviceProvider = services.BuildServiceProvider();
        var registry = new InMemoryModuleRegistry();

        // Act — DIAwareTestModule requires ILogger<DIAwareTestModule> in constructor
        registry.RegisterModule(typeof(DIAwareTestModule), serviceProvider);

        // Assert
        registry.HasModule("test.di-aware").Should().BeTrue("DI-based registration should work! uwu");
        var module = registry.GetModule("test.di-aware") as DIAwareTestModule;
        module.Should().NotBeNull();
        module!.HasLogger.Should().BeTrue("the logger should have been injected~ 💉");
    }

    /// <summary>
    /// RegisterModule(Type) should throw when type doesn't implement IWorkflowModule~ ❌
    /// </summary>
    [Fact]
    public void RegisterModuleByType_InvalidType_ShouldThrow()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();

        // Act & Assert
        var act = () => registry.RegisterModule(typeof(string));
        act.Should().Throw<ArgumentException>()
            .WithMessage("*does not implement IWorkflowModule*");
    }

    #endregion

    #region Observer Notification Tests 👀

    /// <summary>
    /// Observer should receive OnModuleRegistered when a module is registered~ 🔔➕
    /// </summary>
    [Fact]
    public void Observer_ShouldReceiveOnModuleRegistered()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var observer = new RecordingObserver();
        registry.Subscribe(observer);

        // Act
        registry.RegisterModule(new PassThroughModule());

        // Assert
        observer.RegisteredModules.Should().ContainSingle(m => m.ModuleId == "builtin.passthrough");
        observer.UnregisteredModules.Should().BeEmpty();
    }

    /// <summary>
    /// Observer should receive OnModuleUnregistered when a module is removed~ 🔔➖
    /// </summary>
    [Fact]
    public void Observer_ShouldReceiveOnModuleUnregistered()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var observer = new RecordingObserver();
        registry.Subscribe(observer);
        registry.RegisterModule(new PassThroughModule());

        // Act
        registry.UnregisterModule("builtin.passthrough");

        // Assert
        observer.UnregisteredModules.Should().ContainSingle(
            t => t.ModuleId == "builtin.passthrough" && t.Module.ModuleId == "builtin.passthrough");
    }

    /// <summary>
    /// Disposing the subscription should stop observer from receiving notifications~ 🧹
    /// </summary>
    [Fact]
    public void ObserverDispose_ShouldUnsubscribe()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var observer = new RecordingObserver();
        var subscription = registry.Subscribe(observer);

        // Register one module — observer should see it
        registry.RegisterModule(new PassThroughModule());
        observer.RegisteredModules.Should().HaveCount(1);

        // Act — dispose the subscription
        subscription.Dispose();

        // Register another module — observer should NOT see it
        registry.RegisterModule(new TestLogicModule());

        // Assert
        observer.RegisteredModules.Should().HaveCount(1, "observer was unsubscribed! uwu");
    }

    /// <summary>
    /// A throwing observer should NOT prevent other observers from receiving notifications~ 💪
    /// </summary>
    [Fact]
    public void ThrowingObserver_ShouldNotBlockOtherObservers()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var throwingObserver = new ThrowingObserver();
        var normalObserver = new RecordingObserver();
        registry.Subscribe(throwingObserver);
        registry.Subscribe(normalObserver);

        // Act — this should NOT throw despite the first observer blowing up
        registry.RegisterModule(new PassThroughModule());

        // Assert — second observer should still get notified
        normalObserver.RegisteredModules.Should().ContainSingle(m => m.ModuleId == "builtin.passthrough");
    }

    #endregion

    #region Duplicate Registration Tests 🔁

    /// <summary>
    /// Duplicate registration should throw by default (allowOverwrite = false)~ ❌
    /// </summary>
    [Fact]
    public void DuplicateRegistration_ShouldThrowByDefault()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());

        // Act & Assert
        var act = () => registry.RegisterModule(new PassThroughModule());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already registered*");
    }

    /// <summary>
    /// Duplicate registration with allowOverwrite = true should succeed~ ✅
    /// </summary>
    [Fact]
    public void DuplicateRegistration_WithAllowOverwrite_ShouldSucceed()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());

        // Act — this should NOT throw
        var act = () => registry.RegisterModule(new PassThroughModule(), allowOverwrite: true);
        act.Should().NotThrow("allowOverwrite was true~ uwu");

        // Assert — module is still there
        registry.HasModule("builtin.passthrough").Should().BeTrue();
    }

    #endregion

    #region Validation Integration Tests ✅🔗

    /// <summary>
    /// Registering an invalid module should be rejected by the wired-in ModuleValidator~ ❌
    /// </summary>
    [Fact]
    public void RegisterModule_InvalidModule_ShouldBeRejectedByValidator()
    {
        // Arrange — module with uppercase ID that violates naming convention
        var registry = new InMemoryModuleRegistry();
        var badModule = new InvalidIdModule();

        // Act & Assert
        var act = () => registry.RegisterModule(badModule);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*failed validation*");
    }

    /// <summary>
    /// Registering an invalid module with skipValidation should succeed~ 🧪
    /// </summary>
    [Fact]
    public void RegisterModule_InvalidModule_WithSkipValidation_ShouldSucceed()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var badModule = new InvalidIdModule();

        // Act — skipValidation bypasses the validator
        registry.RegisterModule(badModule, skipValidation: true);

        // Assert
        registry.HasModule("INVALID-uppercase-ID").Should().BeTrue("skipValidation was true~ uwu");
    }

    #endregion

    #region Test Helper Classes 🧪

    /// <summary>
    /// 🧪 A simple test module in the "Logic" category~ 🧠
    /// </summary>
    private sealed class TestLogicModule : IWorkflowModule
    {
        public string ModuleId => "test.logic";

        public string DisplayName => "Logic Gate";

        public string Category => "Logic";

        public string Description => "A test logic module for testing category and search.";

        public string Icon => "🧠";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    /// <summary>
    /// 🧪 A test module also in the "Utilities" category~ 🔧
    /// </summary>
    private sealed class TestUtilityModule : IWorkflowModule
    {
        public string ModuleId => "test.utility";

        public string DisplayName => "Test Utility";

        public string Category => "Utilities";

        public string Description => "Another utility module for testing category grouping.";

        public string Icon => "🔧";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    /// <summary>
    /// 🧪 A module that requires ILogger via DI constructor injection~ 💉
    /// </summary>
    public sealed class DIAwareTestModule : IWorkflowModule
    {
        private readonly ILogger<DIAwareTestModule> _logger;

        public DIAwareTestModule(ILogger<DIAwareTestModule> logger)
        {
            _logger = logger;
        }

        public bool HasLogger => _logger != null;

        public string ModuleId => "test.di-aware";

        public string DisplayName => "DI Aware Module";

        public string Category => "Testing";

        public string Description => "A module that requires DI for constructor injection.";

        public string Icon => "💉";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    /// <summary>
    /// 👀 Observer that records all registration/unregistration events~ 📝
    /// </summary>
    private sealed class RecordingObserver : IModuleRegistryObserver
    {
        public List<IWorkflowModule> RegisteredModules { get; } = new();

        public List<(string ModuleId, IWorkflowModule Module)> UnregisteredModules { get; } = new();

        public void OnModuleRegistered(IWorkflowModule module)
        {
            RegisteredModules.Add(module);
        }

        public void OnModuleUnregistered(string moduleId, IWorkflowModule module)
        {
            UnregisteredModules.Add((moduleId, module));
        }
    }

    /// <summary>
    /// 🧪 A module with an invalid (uppercase) ID for testing validator rejection~ ❌
    /// </summary>
    private sealed class InvalidIdModule : IWorkflowModule
    {
        public string ModuleId => "INVALID-uppercase-ID";

        public string DisplayName => "Invalid Module";

        public string Category => "Testing";

        public string Description => "A module with an invalid ID for testing validator rejection.";

        public string Icon => "❌";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => ModuleSchema.Empty;

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    /// <summary>
    /// 💥 Observer that throws on every notification — for testing resilience~ 💪
    /// </summary>
    private sealed class ThrowingObserver : IModuleRegistryObserver
    {
        public void OnModuleRegistered(IWorkflowModule module)
        {
            throw new InvalidOperationException("BOOM! Observer exploded on register~ 💥");
        }

        public void OnModuleUnregistered(string moduleId, IWorkflowModule module)
        {
            throw new InvalidOperationException("BOOM! Observer exploded on unregister~ 💥");
        }
    }

    #endregion
}


