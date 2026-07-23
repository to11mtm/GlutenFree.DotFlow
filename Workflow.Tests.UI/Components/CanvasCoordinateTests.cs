// <copyright file="CanvasCoordinateTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Round-2 G1 — the connect ghost / rubber band must use viewport-relative CLIENT
/// coordinates, because offset coordinates are relative to the event target (a node/port when
/// hovering one), which snapped the ghost to the top-left corner~ ✨.
/// </summary>
public sealed class CanvasCoordinateTests : TestContext
{
    public CanvasCoordinateTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
    }

    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "builtin.log", Name = "A", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "builtin.log", Name = "B", X = 500, Y = 100 });
        return doc;
    }

    [Fact]
    public void ConnectGhost_UsesClientCoordinates_WhenViewportRectKnown()
    {
        // The viewport sits at client (200, 50).
        this.JSInterop.Setup<ViewportRect>("dotflowCanvas.measure", _ => true)
            .SetResult(new ViewportRect(1200, 800, 200, 50));

        var doc = TwoNodeDoc();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        // Start a connection drag (also measures the rect).
        cut.Find("[data-node-id='a'] [data-port-out=output]").PointerDown();

        // Move with the pointer over node b: offsets are TARGET-relative (tiny values —
        // exactly the bug), while client coords are viewport (200,50) + (700, 200).
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs
        {
            ClientX = 900,
            ClientY = 250,
            OffsetX = 4,   // relative to the hovered node — must be ignored
            OffsetY = 7,
        });

        var ghost = cut.Find(".df-connect-ghost path").GetAttribute("d")!;

        // The bezier "d" ends at the cursor point: x = 900-200 = 700, y = 250-50 = 200.
        ghost.Should().EndWith("700 200", because: "the ghost endpoint must track client − viewport-rect, not target offsets~ 🎯");
        ghost.Should().NotEndWith("4 7", because: "target-relative offsets caused the top-left snap (G1)~ 💔");
    }

    [Fact]
    public void ConnectGhost_FallsBackToOffsets_WithoutRect()
    {
        // Loose JS returns default(ViewportRect) → width 0 → rect unknown → offset fallback.
        var doc = TwoNodeDoc();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, new SelectionState())
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("[data-node-id='a'] [data-port-out=output]").PointerDown();
        cut.Find(".df-canvas-viewport").PointerMove(new PointerEventArgs { OffsetX = 640, OffsetY = 480 });

        cut.Find(".df-connect-ghost path").GetAttribute("d")!
            .Should().EndWith("640 480", because: "without a measured rect the offsets are the best available~ 🧪");
    }

    [Fact]
    public void RubberBand_UsesClientCoordinates_WhenViewportRectKnown()
    {
        this.JSInterop.Setup<ViewportRect>("dotflowCanvas.measure", _ => true)
            .SetResult(new ViewportRect(1200, 800, 200, 50));

        var doc = TwoNodeDoc();
        var selection = new SelectionState();
        var cut = this.RenderComponent<CanvasView>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, selection)
            .Add(x => x.Commands, new CommandStack(doc)));

        var viewport = cut.Find(".df-canvas-viewport");
        viewport.PointerDown(new PointerEventArgs { ShiftKey = true, ClientX = 300, ClientY = 150, OffsetX = 100, OffsetY = 100 });

        // Move with target-relative offsets that would otherwise collapse the band.
        viewport.PointerMove(new PointerEventArgs { ClientX = 1000, ClientY = 450, OffsetX = 3, OffsetY = 5 });

        var band = cut.Find(".df-rubberband").GetAttribute("style")!;
        band.Should().Contain("left:100px").And.Contain("top:100px")
            .And.Contain("width:700px").And.Contain("height:300px");
    }
}
