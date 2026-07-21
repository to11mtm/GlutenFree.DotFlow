// <copyright file="FilterModelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.State;

using System;
using FluentAssertions;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 🔍 Phase 3.5.4 — Tests for the framework-free <see cref="ExecutionFilterModel"/> + log classifier~ ✨.
/// </summary>
public sealed class FilterModelTests
{
    private static MonitorRow Row(double? durMs, DateTimeOffset started)
        => new() { ExecutionId = Guid.NewGuid(), State = "Completed", StartedAt = started, DurationMs = durMs };

    [Fact]
    public void Filter_Status_MapsToServerQuery()
    {
        new ExecutionFilterModel { Status = "All" }.ServerStatus.Should().BeNull();
        new ExecutionFilterModel { Status = null }.ServerStatus.Should().BeNull();
        new ExecutionFilterModel { Status = "Failed" }.ServerStatus.Should().Be("Failed");
    }

    [Fact]
    public void Filter_Duration_FiltersClientSide()
    {
        var model = new ExecutionFilterModel { MinDurationMs = 1000 };
        var t = DateTimeOffset.UtcNow;

        var rows = model.Apply(new[] { Row(500, t), Row(1500, t), Row(2000, t) });

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.DurationMs >= 1000);
    }

    [Fact]
    public void Sort_ByColumn_Orders()
    {
        var t = DateTimeOffset.UtcNow;
        var model = new ExecutionFilterModel { SortColumn = "Duration", Descending = false };

        var rows = model.Apply(new[] { Row(3000, t), Row(1000, t), Row(2000, t) });

        rows[0].DurationMs.Should().Be(1000);
        rows[2].DurationMs.Should().Be(3000);
    }

    [Fact]
    public void ToggleSort_SameColumn_FlipsDirection()
    {
        var model = new ExecutionFilterModel { SortColumn = "Started", Descending = true };

        model.ToggleSort("Started");
        model.Descending.Should().BeFalse();

        model.ToggleSort("Duration");
        model.SortColumn.Should().Be("Duration");
        model.Descending.Should().BeTrue();
    }

    [Theory]
    [InlineData("node enrich failed: boom", "Error")]
    [InlineData("execution completed (12ms)", "Info")]
    [InlineData("node validate started", "Debug")]
    [InlineData("execution cancelled", "Warning")]
    public void RunLogClassifier_LevelOf_Classifies(string text, string expected)
        => RunLogClassifier.LevelOf(text).Should().Be(expected);
}
