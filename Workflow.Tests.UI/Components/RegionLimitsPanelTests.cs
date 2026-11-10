// <copyright file="RegionLimitsPanelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// Stage 7.5 — a streaming region's time limits are edited on its source node (in seconds, stored
/// as milliseconds in the node's metadata; blank = the server's setting, 0 = off), and only there.
/// </summary>
public sealed class RegionLimitsPanelTests : TestContext
{
    public RegionLimitsPanelTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Scripts.State.ScriptStudioHandoff());
        this.Services.AddSingleton(new Workflow.UI.Client.Linq.State.LinqStudioHandoff());
    }

    private static PortDefinitionDto Port(string name, bool streaming) => new(name, name, "object", null, false, null, streaming);

    /// <summary>batch "start" → stream source "src" → stream "sink".</summary>
    private static (DesignerDocument Doc, CommandStack Cmd) Setup()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "start", ModuleId = "m.batch", Name = "Start", Schema = new([Port("input", false)], [Port("output", false)], new()) });
        doc.Nodes.Add(new DesignerNode { Id = "src", ModuleId = "m.source", Name = "Source", Schema = new([Port("input", false)], [Port("items", true)], new()), X = 200 });
        doc.Nodes.Add(new DesignerNode { Id = "sink", ModuleId = "m.sink", Name = "Sink", Schema = new([Port("items", true)], [Port("count", false)], new()), X = 400 });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "start", SourcePortName = "output", TargetNodeId = "src", TargetPortName = "input" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "src", SourcePortName = "items", TargetNodeId = "sink", TargetPortName = "items" });
        return (doc, new CommandStack(doc));
    }

    private IRenderedComponent<PropertiesPanel> Render(DesignerDocument doc, CommandStack cmd, string selected)
    {
        var selection = new SelectionState();
        selection.SelectNode(selected);
        return this.RenderComponent<PropertiesPanel>(p => p.Add(x => x.Document, doc).Add(x => x.Selection, selection).Add(x => x.Commands, cmd));
    }

    [Theory]
    [InlineData("src", true)]
    [InlineData("sink", false)]
    [InlineData("start", false)]
    public void TheLimits_AreOnlyOnARegionsSourceNode(string node, bool shown)
    {
        var (doc, cmd) = Setup();

        this.Render(doc, cmd, node).FindAll("[data-testid=region-limits]").Should().HaveCount(shown ? 1 : 0);
    }

    [Fact]
    public void SettingALimit_StoresMilliseconds_ZeroMeansOff_BlankClears_AndUndoWorks()
    {
        var (doc, cmd) = Setup();
        var cut = this.Render(doc, cmd, "src");
        var src = doc.FindNode("src")!;

        cut.Find("[data-testid=region-stall]").Change("90");
        src.Metadata[RegionTimeouts.StallKey].Should().Be("90000");

        cut.Find("[data-testid=region-total]").Change("0");
        src.Metadata[RegionTimeouts.TotalKey].Should().Be("0", because: "0 turns the server's overall limit off for this region");

        cut.Find("[data-testid=region-stall]").Change(string.Empty);
        src.Metadata.ContainsKey(RegionTimeouts.StallKey).Should().BeFalse(because: "blank means: use the server's setting");

        cmd.Undo();
        src.Metadata[RegionTimeouts.StallKey].Should().Be("90000");
        RegionTimeouts.SecondsText(src, RegionTimeouts.StallKey).Should().Be("90");
    }
}
