// <copyright file="TestRunStateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts;

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Scripts.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.4.3 — Tests for the framework-free <see cref="TestRunState"/>~ ✨.
/// </summary>
public sealed class TestRunStateTests
{
    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static ScriptTestResultDto ResultWithLogs()
        => new(
            true,
            El("{\"ok\":true}"),
            new List<ScriptLogEntryDto>
            {
                new("Info", "processing order-1"),
                new("Warning", "slow response"),
                new("Error", "retrying order-2"),
            },
            new Dictionary<string, JsonElement>(),
            5.0,
            null);

    [Fact]
    public void ParseInputs_Empty_ReturnsNull()
    {
        var state = new TestRunState { Inputs = "  " };
        state.ParseInputs(out var error).Should().BeNull();
        error.Should().BeNull();

        new TestRunState { Inputs = "{}" }.ParseInputs(out _).Should().BeNull();
    }

    [Fact]
    public void ParseInputs_Valid_Parses()
    {
        var state = new TestRunState { Inputs = "{\"orders\":[{\"amount\":10}]}" };
        var inputs = state.ParseInputs(out var error);
        error.Should().BeNull();
        inputs.Should().ContainKey("orders");
    }

    [Fact]
    public void ParseInputs_Invalid_SetsError()
    {
        var state = new TestRunState { Inputs = "{ not json" };
        state.ParseInputs(out var error).Should().BeNull();
        error.Should().NotBeNull();
        error.Should().Contain("JSON");
    }

    [Fact]
    public void FilteredLogs_LevelFilter_Filters()
    {
        var state = new TestRunState();
        state.CompleteRun(ResultWithLogs());

        state.FilteredLogs("Warning", null).Should().ContainSingle(l => l.Message.Contains("slow"));
        state.FilteredLogs("All", null).Should().HaveCount(3);
    }

    [Fact]
    public void FilteredLogs_Search_Filters()
    {
        var state = new TestRunState();
        state.CompleteRun(ResultWithLogs());

        state.FilteredLogs(null, "order-2").Should().ContainSingle(l => l.Level == "Error");
        state.FilteredLogs(null, "order").Should().HaveCount(2);
    }

    [Fact]
    public void ToConfig_Maps_Toggles()
    {
        var state = new TestRunState { TimeoutSeconds = 15, AllowNetwork = true, AllowFileSystem = false };
        var cfg = state.ToConfig();
        cfg.TimeoutSeconds.Should().Be(15);
        cfg.AllowNetwork.Should().Be(true);
        cfg.AllowFileSystem.Should().Be(false);
    }

    [Fact]
    public void BeginRun_ClearsPrevious_AndRaisesChanged()
    {
        var state = new TestRunState();
        state.CompleteRun(ResultWithLogs());
        var raised = 0;
        state.Changed += () => raised++;

        state.BeginRun();

        state.Running.Should().BeTrue();
        state.Result.Should().BeNull();
        raised.Should().Be(1);
    }
}
