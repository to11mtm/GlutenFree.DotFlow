// <copyright file="ReplayTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.Components;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Execution.Components;
using Xunit;

/// <summary>
/// 🎬 Phase 3.5.5 — bUnit tests for the replay timeline + the designer→monitor deep link~ ✨.
/// </summary>
public sealed class ReplayTests : TestContext
{
    public ReplayTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-20T16:00:00Z");

    private static List<NodeExecutionRecordDto> Nodes() => new()
    {
        new("validate", "Completed", T0, T0.AddSeconds(1), 1000, null, null, null, null, null),
        new("enrich", "Failed", T0.AddSeconds(1), T0.AddSeconds(2), 1000, null, null, "boom", null, null),
    };

    [Fact]
    public void Replay_Scrub_UpdatesCurrentNode()
    {
        var cut = this.RenderComponent<ReplayTimeline>(p => p.Add(x => x.Nodes, Nodes()));

        cut.Find("[data-testid=replay-current]").TextContent.Should().Contain("validate");
        cut.Find("[data-testid=replay-label]").TextContent.Should().Contain("step 1 / 2");

        cut.Find("[data-testid=replay-fwd]").Click();

        cut.Find("[data-testid=replay-current]").TextContent.Should().Contain("enrich");
        cut.Find("[data-testid=replay-label]").TextContent.Should().Contain("step 2 / 2");
    }

    [Fact]
    public void Replay_DisabledWhileRunning()
    {
        var cut = this.RenderComponent<ReplayTimeline>(p => p
            .Add(x => x.Nodes, Nodes())
            .Add(x => x.Running, true));

        cut.Find("[data-testid=replay-disabled]").Should().NotBeNull();
        cut.FindAll("[data-testid=replay-fwd]").Should().BeEmpty();
    }

    [Fact]
    public void Replay_RaisesOnStep()
    {
        string? stepped = null;
        var cut = this.RenderComponent<ReplayTimeline>(p => p
            .Add(x => x.Nodes, Nodes())
            .Add(x => x.OnStep, id => stepped = id));

        cut.Find("[data-testid=replay-last]").Click();

        stepped.Should().Be("enrich");
    }

    [Fact]
    public void Designer_HistoryLink_NavigatesToMonitor()
    {
        var id = Guid.NewGuid();
        var page = new PageDto<ExecutionDto>(
            new List<ExecutionDto> { new(id, Guid.NewGuid(), "Completed", T0, T0.AddSeconds(1), null) },
            1, 1, 20, 1);
        var handler = FakeHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(page, ApiHttp.Json));
        this.Services.AddSingleton(new ExecutionsClient(handler.CreateClient()));

        var cut = this.RenderComponent<ExecutionHistory>(p => p.Add(x => x.WorkflowId, Guid.NewGuid()));
        cut.WaitForAssertion(() => cut.Find("[data-testid=open-in-monitor]"));
        cut.Find("[data-testid=open-in-monitor]").Click();

        this.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith($"/monitor/{id}");
    }
}
