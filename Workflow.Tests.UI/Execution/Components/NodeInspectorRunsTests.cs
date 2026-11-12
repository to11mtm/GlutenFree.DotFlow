// <copyright file="NodeInspectorRunsTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.Components;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Execution.Components;
using Xunit;

/// <summary>
/// A node has a history row per run (each loop iteration, each retry attempt). Every row can be
/// opened, and a failed attempt that was retried is marked.
/// </summary>
public sealed class NodeInspectorRunsTests : TestContext
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static NodeExecutionRecordDto Run(int iteration, string state, int second, string? error = null)
        => new(
            "body",
            state,
            T0.AddSeconds(second),
            T0.AddSeconds(second),
            5,
            new Dictionary<string, JsonElement> { ["item"] = JsonSerializer.SerializeToElement($"item-{iteration}") },
            null,
            error,
            "loop",
            iteration);

    [Fact]
    public void EachRun_CanBeOpened_AndARetriedAttemptIsMarked()
    {
        var nodes = new List<NodeExecutionRecordDto>
        {
            Run(1, "Completed", 1),
            Run(2, "Failed", 2, "boom"),
            Run(2, "Completed", 3),
            Run(3, "Failed", 4, "gave up"),
        };
        var selected = new List<string>();

        var cut = this.RenderComponent<NodeInspector>(p => p
            .Add(x => x.Nodes, nodes)
            .Add(x => x.OnSelect, (string id) => selected.Add(id)));

        cut.FindAll("[data-testid=node-body]").Should().HaveCount(4, "one row per run");
        cut.Find("[data-testid=node-inputs]").TextContent.Should().Contain("item-1");

        cut.Find("[data-row='1']").Click();
        cut.Find("[data-testid=node-inputs]").TextContent.Should().Contain("item-2");
        cut.Find("[data-testid=node-error]").TextContent.Should().Contain("boom", "the second row is the failed attempt");
        selected.Should().Equal("body");

        cut.Find("[data-row='2']").Click();
        cut.FindAll("[data-testid=node-error]").Should().BeEmpty("the retry succeeded");

        var retried = cut.FindAll("[data-testid=node-retried]");
        retried.Should().ContainSingle("only iteration 2's failed attempt was followed by another run; iteration 3's failure was final");
        cut.Find("[data-row='1']").InnerHtml.Should().Contain("retried");
    }

    [Fact]
    public void NestedLoopRows_ShowTheirWholePath_AndOnlyARetryInTheSameRunCounts()
    {
        var nodes = new List<NodeExecutionRecordDto>
        {
            Run(1, "Failed", 1) with { LoopId = "inner", ScopePath = "outer#1/inner#1" },
            Run(1, "Completed", 2) with { LoopId = "inner", ScopePath = "outer#2/inner#1" },
            Run(1, "Failed", 3) with { LoopId = "inner", ScopePath = "outer#3/inner#1" },
            Run(1, "Completed", 4) with { LoopId = "inner", ScopePath = "outer#3/inner#1" },
        };

        var cut = this.RenderComponent<NodeInspector>(p => p.Add(x => x.Nodes, nodes));

        cut.FindAll("[data-testid=node-scope]").Select(e => e.TextContent)
            .Should().Equal("#1 › #1", "#2 › #1", "#3 › #1", "#3 › #1");
        cut.Find("[data-testid=node-detail-scope]").TextContent.Should().Contain("outer#1 › inner#1");

        // Row 0 failed in outer#1; the next inner#1 row ran in outer#2, a different run — not a retry.
        cut.Find("[data-row='0']").InnerHtml.Should().NotContain("retried");
        cut.Find("[data-row='2']").InnerHtml.Should().Contain("retried", "outer#3/inner#1 ran again");
    }
}
