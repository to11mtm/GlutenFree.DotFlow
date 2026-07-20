// <copyright file="OverlayRenderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Execution.Components;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.c.1 — Run-overlay rendering + canvas painting specs~ ✨.
/// </summary>
public sealed class OverlayRenderTests : TestContext
{
    public OverlayRenderTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
    }

    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 400, Y = 100 });
        return doc;
    }

    [Fact]
    public void Overlay_ShowsProgress_AndNodeStatuses()
    {
        var doc = TwoNodeDoc();
        var run = new RunState();
        run.Progress(50, "a", 1, 2);
        run.NodeCompleted("a", 42);

        var cut = this.RenderComponent<RunOverlay>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Run, run));

        cut.Markup.Should().Contain("50%");
        cut.FindAll("[data-testid=runnode-a]").Should().NotBeEmpty();
        cut.Markup.Should().Contain("42ms");
    }

    [Fact]
    public void Overlay_ShowsCancel_WhenRunning_CloseAlways()
    {
        var doc = TwoNodeDoc();
        var run = new RunState();

        var cut = this.RenderComponent<RunOverlay>(p => p.Add(x => x.Document, doc).Add(x => x.Run, run));

        cut.FindAll("[data-testid=run-cancel]").Should().NotBeEmpty();
        cut.FindAll("[data-testid=run-close]").Should().NotBeEmpty();
    }

    [Fact]
    public void Overlay_HidesCancel_WhenTerminal()
    {
        var doc = TwoNodeDoc();
        var run = new RunState();
        run.MarkCompleted(100);

        var cut = this.RenderComponent<RunOverlay>(p => p.Add(x => x.Document, doc).Add(x => x.Run, run));

        cut.FindAll("[data-testid=run-cancel]").Should().BeEmpty();
    }

    [Fact]
    public void Overlay_ShowsReconnectingChip()
    {
        var doc = TwoNodeDoc();
        var run = new RunState();

        var cut = this.RenderComponent<RunOverlay>(p => p
            .Add(x => x.Document, doc).Add(x => x.Run, run).Add(x => x.Reconnecting, true));

        cut.FindAll("[data-testid=reconnecting]").Should().NotBeEmpty();
    }

    [Fact]
    public void Canvas_PaintsNodeState_FromRunState()
    {
        var doc = TwoNodeDoc();
        var run = new RunState();
        run.NodeStarted("a");
        run.NodeFailed("b", "boom", 10);

        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.NodeStateClass, run.CssClassFor));

        cut.Find("[data-node-id=a]").ClassList.Should().Contain("df-node--running");
        cut.Find("[data-node-id=b]").ClassList.Should().Contain("df-node--failed");
    }
}
