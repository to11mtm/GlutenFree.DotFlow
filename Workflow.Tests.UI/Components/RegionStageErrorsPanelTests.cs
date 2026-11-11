// <copyright file="RegionStageErrorsPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Execution.Components;
using Xunit;

/// <summary>
/// Stage 7.8 — the On error panel for a stage of a streaming region: opt into the region's (source's)
/// error handling (shown read-only with a link to the source), choose what happens after the region
/// on continue / an error handler, and see what retry does for this kind of stage. The execution
/// detail's node inspector shows why a stage was skipped.
/// </summary>
public sealed class RegionStageErrorsPanelTests : TestContext
{
    public RegionStageErrorsPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static PortDefinitionDto Port(string name, bool streaming) => new(name, name, "object", null, false, null, streaming);

    /// <summary>start (batch) → src (stream source, Continue) → each (per-item) → mix (whole-stream) → sink.</summary>
    private static (DesignerDocument Doc, CommandStack Cmd, SelectionState Sel) Setup(string selected)
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "start", ModuleId = "m.batch", Name = "Start", Schema = new([Port("input", false)], [Port("output", false)], new()) });
        doc.Nodes.Add(new DesignerNode
        {
            Id = "src", ModuleId = "m.source", Name = "Reader", Schema = new([Port("input", false)], [Port("items", true)], new()),
            ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.Continue)),
        });
        doc.Nodes.Add(new DesignerNode { Id = "each", ModuleId = "m.each", Name = "Each", Schema = new([Port("items", true)], [Port("items", true)], new(), "perItem") });
        doc.Nodes.Add(new DesignerNode { Id = "mix", ModuleId = "m.mix", Name = "Mix", Schema = new([Port("items", true)], [Port("items", true)], new(), "wholeStream") });
        doc.Nodes.Add(new DesignerNode { Id = "sink", ModuleId = "m.sink", Name = "Sink", Schema = new([Port("items", true)], [Port("count", false)], new()) });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "start", SourcePortName = "output", TargetNodeId = "src", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "src", SourcePortName = "items", TargetNodeId = "each", TargetPortName = "items" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "each", SourcePortName = "items", TargetNodeId = "mix", TargetPortName = "items" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "mix", SourcePortName = "items", TargetNodeId = "sink", TargetPortName = "items" });
        var sel = new SelectionState();
        sel.SelectNode(selected);
        return (doc, new CommandStack(doc), sel);
    }

    private IRenderedComponent<PropertiesPanel> Render(DesignerDocument doc, CommandStack cmd, SelectionState sel)
        => this.RenderComponent<PropertiesPanel>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, sel).Add(x => x.Commands, cmd));

    [Theory]
    [InlineData("mix", true)]
    [InlineData("src", false)]
    [InlineData("start", false)]
    public void TheUseTheRegionsOption_IsOnStagesOtherThanTheSource(string node, bool shown)
    {
        var (doc, cmd, sel) = Setup(node);

        this.Render(doc, cmd, sel).FindAll("[data-testid=on-error-use-region]").Should().HaveCount(shown ? 1 : 0);
    }

    [Fact]
    public void UsingTheRegions_ShowsTheSourcesSettingReadOnly_WithALinkToTheSource()
    {
        var (doc, cmd, sel) = Setup("mix");
        var cut = this.Render(doc, cmd, sel);

        cut.Find("[data-testid=on-error-use-region]").Change(true);

        doc.FindNode("mix")!.Metadata[RegionStageErrors.UseRegionKey].Should().Be("true");
        cut.FindAll("[data-testid=on-error-behavior]").Should().BeEmpty(because: "the stage's own settings don't apply while it uses the region's");
        cut.Find("[data-testid=on-error-region-summary]").TextContent.Should().Contain("Continue").And.Contain("Reader");
        cut.FindAll("[data-testid=on-error-after-region]").Should().ContainSingle(because: "the effective behaviour (the source's) is Continue");

        cut.Find("[data-testid=on-error-region-source]").Click();
        sel.IsNodeSelected("src").Should().BeTrue();

        cmd.Undo();
        doc.FindNode("mix")!.Metadata.ContainsKey(RegionStageErrors.UseRegionKey).Should().BeFalse();
    }

    [Fact]
    public void AfterTheRegion_DefaultsToRunWhatFollows_AndCanSkipIt()
    {
        var (doc, cmd, sel) = Setup("src");
        var cut = this.Render(doc, cmd, sel);

        var select = cut.Find("[data-testid=on-error-after-region]");
        select.QuerySelectorAll("option").First(o => o.HasAttribute("selected")).TextContent.Should().Contain("run what follows");

        select.Change(RegionStageErrors.SkipWhatFollowsKey);
        doc.FindNode("src")!.Metadata[RegionStageErrors.SkipWhatFollowsKey].Should().Be("true");

        cut.Find("[data-testid=on-error-after-region]").Change(string.Empty);
        doc.FindNode("src")!.Metadata.ContainsKey(RegionStageErrors.SkipWhatFollowsKey).Should().BeFalse();
    }

    [Theory]
    [InlineData("each", "retries the failing item in place")]
    [InlineData("mix", "re-runs the whole region from 'Reader'")]
    public void RetryHelp_SaysWhatRetryDoesForThisStage(string node, string expected)
    {
        var (doc, cmd, sel) = Setup(node);
        doc.FindNode(node)!.ErrorHandling = NodeErrorHandling.Write(new NodeErrorHandling.Settings(NodeErrorHandling.Retry));

        this.Render(doc, cmd, sel).Find("[data-testid=on-error-region-retry]").TextContent.Should().Contain(expected);
    }

    [Fact]
    public void TheNodeInspector_ShowsWhyAStageWasSkipped()
    {
        var records = new List<NodeExecutionRecordDto>
        {
            new("sink", "Skipped", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null, null, null, null, null) { SkipReason = "skipped: upstream stage 'mix' failed" },
        };

        var cut = this.RenderComponent<NodeInspector>(p => p.Add(x => x.Nodes, records));
        cut.Find("[data-testid=node-sink]").Click();

        cut.Find("[data-testid=node-skip-reason]").TextContent.Should().Contain("upstream stage 'mix' failed");
    }
}
