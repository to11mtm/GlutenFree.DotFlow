// <copyright file="VariableUpdatesTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using FluentAssertions;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// 💾 Phase 1.5.0 — Tests for <see cref="ModuleResult.VariableUpdates"/>!
/// Verifies the mechanism that lets modules declare variable mutations
/// without directly mutating the read-only execution context~ ✨💖
/// </summary>
public sealed class VariableUpdatesTests
{
    #region ModuleResult Factory Tests 🏭

    /// <summary>
    /// <c>ModuleResult.Ok(outputs)</c> without variable updates should have null VariableUpdates (backwards compat)~ 🔙
    /// </summary>
    [Fact]
    public void Ok_WithoutVariableUpdates_ShouldHaveNullVariableUpdates()
    {
        // Arrange & Act
        var result = ModuleResult.Ok(new Dictionary<string, object?> { ["out"] = "val" });

        // Assert
        result.VariableUpdates.Should().BeNull("no variable updates were provided~ 💖");
        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// <c>ModuleResult.Ok(outputs, variableUpdates)</c> should carry the updates~ 💾
    /// </summary>
    [Fact]
    public void Ok_WithVariableUpdates_ShouldSetProperty()
    {
        // Arrange
        var outputs = new Dictionary<string, object?> { ["out"] = "val" };
        var varUpdates = new Dictionary<string, object?> { ["count"] = 42, ["name"] = "Ami" };

        // Act
        var result = ModuleResult.Ok(outputs, varUpdates);

        // Assert
        result.Success.Should().BeTrue();
        result.VariableUpdates.Should().NotBeNull();
        result.VariableUpdates.Should().HaveCount(2);
        result.VariableUpdates!["count"].Should().Be(42);
        result.VariableUpdates["name"].Should().Be("Ami");
    }

    /// <summary>
    /// <c>ModuleResult.Ok(outputs, metrics, variableUpdates)</c> — the full overload~ 📊💾
    /// </summary>
    [Fact]
    public void Ok_WithMetricsAndVariableUpdates_ShouldSetBoth()
    {
        // Arrange
        var outputs = new Dictionary<string, object?> { ["x"] = 1 };
        var metrics = ExecutionMetrics.FromDuration(TimeSpan.FromMilliseconds(50));
        var varUpdates = new Dictionary<string, object?> { ["flag"] = true };

        // Act
        var result = ModuleResult.Ok(outputs, metrics, varUpdates);

        // Assert
        result.Success.Should().BeTrue();
        result.Metrics.Should().NotBeNull();
        result.Metrics!.Duration.Should().BeCloseTo(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(5));
        result.VariableUpdates.Should().ContainKey("flag");
    }

    /// <summary>
    /// <c>ModuleResult.Ok(outputs, metrics)</c> without variable updates — backwards compat~ 🔙
    /// </summary>
    [Fact]
    public void Ok_WithMetricsButNoVariableUpdates_ShouldHaveNullVariableUpdates()
    {
        // Arrange
        var outputs = new Dictionary<string, object?> { ["x"] = 1 };
        var metrics = ExecutionMetrics.FromDuration(TimeSpan.FromMilliseconds(10));

        // Act
        var result = ModuleResult.Ok(outputs, metrics);

        // Assert
        result.VariableUpdates.Should().BeNull("only metrics were provided~ ✨");
    }

    /// <summary>
    /// <c>ModuleResult.Fail</c> should have null VariableUpdates (failures don't mutate)~ ❌
    /// </summary>
    [Fact]
    public void Fail_ShouldHaveNullVariableUpdates()
    {
        // Act
        var result = ModuleResult.Fail("something broke~ 😿");

        // Assert
        result.VariableUpdates.Should().BeNull("failed modules don't get to mutate variables~ 💔");
        result.Success.Should().BeFalse();
    }

    /// <summary>
    /// Setting null as a variable value should be allowed (represents deletion)~ 🗑️
    /// </summary>
    [Fact]
    public void Ok_WithNullVariableValue_ShouldBeValid()
    {
        // Arrange
        var outputs = new Dictionary<string, object?>();
        var varUpdates = new Dictionary<string, object?> { ["toDelete"] = null };

        // Act
        var result = ModuleResult.Ok(outputs, varUpdates);

        // Assert
        result.VariableUpdates.Should().ContainKey("toDelete");
        result.VariableUpdates!["toDelete"].Should().BeNull("null value represents deletion~ UwU");
    }

    #endregion
}

