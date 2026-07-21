// <copyright file="MinimapTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.c.3 — Lightweight minimap specs (Q6 compromise)~ ✨.
/// </summary>
public sealed class MinimapTests : TestContext
{
    private static DesignerDocument TwoNodeDoc()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A", X = 100, Y = 100 });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B", X = 800, Y = 500 });
        return doc;
    }

    [Fact]
    public void Minimap_RendersRectPerNode_PlusFrame()
    {
        var cut = this.RenderComponent<Minimap>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.Transform, CanvasTransform.Identity));

        // 2 node rects + background rect + viewport frame rect = at least 4 rects.
        cut.FindAll("rect").Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Minimap_Click_RaisesNavigate()
    {
        Point? nav = null;
        var cut = this.RenderComponent<Minimap>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.Transform, CanvasTransform.Identity)
            .Add(x => x.OnNavigate, pt => nav = pt));

        cut.Find(".df-minimap__svg").Click(new MouseEventArgs { OffsetX = 90, OffsetY = 60 });

        nav.Should().NotBeNull();
    }

    [Fact]
    public void Minimap_Collapse_HidesSvg()
    {
        var cut = this.RenderComponent<Minimap>(p => p
            .Add(x => x.Document, TwoNodeDoc())
            .Add(x => x.Transform, CanvasTransform.Identity));

        cut.FindAll(".df-minimap__svg").Should().NotBeEmpty();
        cut.Find("[data-testid=minimap-toggle]").Click();
        cut.FindAll(".df-minimap__svg").Should().BeEmpty();
    }
}
