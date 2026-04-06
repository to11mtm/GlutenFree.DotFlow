// <copyright file="ModuleContractTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// 🌸 Phase 1.4.1 — Tests for IWorkflowModule &amp; ModuleResult enhancements!
/// Validates the Version property, ValidateConfiguration default, Dependencies default,
/// ExecutionMetrics, and ModuleResult.Ok overloads. UwU ✨💖
/// </summary>
public class ModuleContractTests
{
    #region IWorkflowModule.Version Tests 🏷️

    /// <summary>
    /// PassThroughModule should expose a non-null Version (1.0.0)~ 🏷️✨
    /// </summary>
    [Fact]
    public void PassThroughModule_Version_ShouldNotBeNull()
    {
        // Arrange
        var module = new PassThroughModule();

        // Act
        var version = module.Version;

        // Assert — version is set and equals 1.0.0
        version.Should().NotBeNull("every module must declare a version! uwu");
        version.Major.Should().Be(1);
        version.Minor.Should().Be(0);
        version.Build.Should().Be(0);
    }

    /// <summary>
    /// A custom module with a different version should report it correctly~ 🔖
    /// </summary>
    [Fact]
    public void CustomModule_Version_ShouldReportCorrectVersion()
    {
        // Arrange
        var module = new VersionedTestModule(new Version(2, 5, 3));

        // Act & Assert
        module.Version.Should().Be(new Version(2, 5, 3));
    }

    #endregion

    #region IWorkflowModule.ValidateConfiguration Default Tests ✅

    /// <summary>
    /// The default ValidateConfiguration should return Success() — non-breaking! 💖
    /// </summary>
    [Fact]
    public void ValidateConfiguration_Default_ShouldReturnSuccess()
    {
        // Arrange — PassThroughModule uses the default interface implementation
        IWorkflowModule module = new PassThroughModule();

        // Act
        var result = module.ValidateConfiguration(new Dictionary<string, object?>());

        // Assert
        result.IsValid.Should().BeTrue("default ValidateConfiguration returns success~ uwu");
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// A module that overrides ValidateConfiguration can return failures~ ❌
    /// </summary>
    [Fact]
    public void ValidateConfiguration_WithCustomOverride_ShouldReturnErrors()
    {
        // Arrange
        IWorkflowModule module = new StrictConfigModule();

        // Act — passing an empty config should fail because "requiredKey" is missing
        var result = module.ValidateConfiguration(new Dictionary<string, object?>());

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("requiredKey"));
    }

    #endregion

    #region IWorkflowModule.Dependencies Default Tests 🔗

    /// <summary>
    /// The default Dependencies property should return an empty list~ 💖
    /// </summary>
    [Fact]
    public void Dependencies_Default_ShouldReturnEmptyList()
    {
        // Arrange
        IWorkflowModule module = new PassThroughModule();

        // Act
        var deps = module.Dependencies;

        // Assert
        deps.Should().NotBeNull("Dependencies should never be null, just empty! uwu");
        deps.Should().BeEmpty("PassThroughModule has no dependencies~");
    }

    #endregion

    #region ExecutionMetrics Tests 📊

    /// <summary>
    /// ExecutionMetrics should capture duration, optional memory, and custom metrics~ 📊
    /// </summary>
    [Fact]
    public void ExecutionMetrics_Creation_ShouldSetAllProperties()
    {
        // Arrange & Act
        var customMetrics = HashMap<string, object>.Empty
            .Add("rows_processed", 42)
            .Add("api_calls", 3);

        var metrics = new ExecutionMetrics
        {
            Duration = TimeSpan.FromMilliseconds(250),
            MemoryBytes = 1024L,
            CustomMetrics = customMetrics,
        };

        // Assert
        metrics.Duration.Should().Be(TimeSpan.FromMilliseconds(250));
        metrics.MemoryBytes.Should().Be(1024L);
        metrics.CustomMetrics.Should().NotBeNull();
        metrics.CustomMetrics!.Value.Find("rows_processed").IsSome.Should().BeTrue();
    }

    /// <summary>
    /// ExecutionMetrics.FromDuration should create a metrics instance with just duration~ ⏱️
    /// </summary>
    [Fact]
    public void ExecutionMetrics_FromDuration_ShouldSetOnlyDuration()
    {
        // Arrange & Act
        var metrics = ExecutionMetrics.FromDuration(TimeSpan.FromSeconds(1.5));

        // Assert
        metrics.Duration.Should().Be(TimeSpan.FromSeconds(1.5));
        metrics.MemoryBytes.Should().BeNull("FromDuration doesn't set memory~ uwu");
        metrics.CustomMetrics.Should().BeNull("FromDuration doesn't set custom metrics~");
    }

    #endregion

    #region ModuleResult.Ok Overloads Tests 🎯

    /// <summary>
    /// ModuleResult.Ok with metrics should store both outputs AND metrics~ 📊
    /// </summary>
    [Fact]
    public void ModuleResult_OkWithMetrics_ShouldIncludeMetrics()
    {
        // Arrange
        var outputs = new Dictionary<string, object?> { ["result"] = "hello" };
        var metrics = ExecutionMetrics.FromDuration(TimeSpan.FromMilliseconds(100));

        // Act
        var result = ModuleResult.Ok(outputs, metrics);

        // Assert
        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().Be("hello");
        result.Metrics.Should().NotBeNull("Ok with metrics should set Metrics! uwu");
        result.Metrics!.Duration.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// ModuleResult.Ok without metrics should still work (backwards compat)~ 💖
    /// </summary>
    [Fact]
    public void ModuleResult_OkWithoutMetrics_ShouldHaveNullMetrics()
    {
        // Arrange
        var outputs = new Dictionary<string, object?> { ["result"] = "world" };

        // Act
        var result = ModuleResult.Ok(outputs);

        // Assert
        result.Success.Should().BeTrue();
        result.Outputs["result"].Should().Be("world");
        result.Metrics.Should().BeNull("Ok without metrics param should be null~ uwu");
    }

    /// <summary>
    /// ModuleResult.Fail should not have metrics~ ❌
    /// </summary>
    [Fact]
    public void ModuleResult_Fail_ShouldHaveNullMetrics()
    {
        // Act
        var result = ModuleResult.Fail("oops", new InvalidOperationException("boom"));

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("oops");
        result.Exception.Should().BeOfType<InvalidOperationException>();
        result.Metrics.Should().BeNull("failures don't get metrics by default~ uwu");
    }

    #endregion

    #region Test Helper Modules 🧪

    /// <summary>
    /// 🧪 A test module with a configurable version for testing Version property~ 🔖
    /// </summary>
    private sealed class VersionedTestModule : IWorkflowModule
    {
        public VersionedTestModule(Version version)
        {
            Version = version;
        }

        public string ModuleId => "test.versioned";

        public string DisplayName => "Versioned Test Module";

        public string Category => "Testing";

        public string Description => "A module with a configurable version for testing.";

        public string Icon => "🔖";

        public Version Version { get; }

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    /// <summary>
    /// 🧪 A test module with strict config validation — requires "requiredKey"~ ❌
    /// </summary>
    private sealed class StrictConfigModule : IWorkflowModule
    {
        public string ModuleId => "test.strict-config";

        public string DisplayName => "Strict Config Module";

        public string Category => "Testing";

        public string Description => "A module that requires 'requiredKey' in configuration.";

        public string Icon => "🔒";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);

        /// <summary>
        /// Override: validates that 'requiredKey' exists in configuration~ 🔒
        /// </summary>
        public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
        {
            if (!configuration.ContainsKey("requiredKey"))
            {
                return ValidationResult.Failure(
                    new ValidationError("CFG001", "Missing 'requiredKey' in configuration"));
            }

            return ValidationResult.Success();
        }

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    #endregion
}



