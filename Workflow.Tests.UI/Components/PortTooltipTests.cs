// <copyright file="PortTooltipTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 FanOut clarity K2 — canvas ports surface their schema descriptions as tooltips~ 🔌.
/// </summary>
public sealed class PortTooltipTests : TestContext
{
    public PortTooltipTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new Workflow.UI.Client.Services.PaletteDragState());
    }

    private static ModuleSchemaDto Schema()
        => new(
            new List<PortDefinitionDto>
            {
                new("items", "Items", "object", "The collection; each element becomes one parallel run~", false, null),
            },
            new List<PortDefinitionDto>
            {
                new("branch", "Branch", "object", "Runs once per item — receives item and index~", false, null),
                new("done", "Done", "object", null, false, null),
            },
            new List<ModulePropertyDefinitionDto>());

    [Fact]
    public void Ports_WithSchemaDescriptions_GetTitleTooltips()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "fan", ModuleId = "builtin.fanout", Name = "Fan", Schema = Schema() });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-port-in=items]").GetAttribute("title").Should().Contain("each element becomes one parallel run");
        cut.Find("[data-port-out=branch]").GetAttribute("title").Should().Contain("once per item");
    }

    [Fact]
    public void Ports_WithoutDescription_GetNoTitleAttribute()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "fan", ModuleId = "builtin.fanout", Name = "Fan", Schema = Schema() });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-port-out=done]").HasAttribute("title").Should().BeFalse();
    }

    [Fact]
    public void UnknownModule_FallbackPorts_HaveNoTooltip()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "x", ModuleId = "nope", Name = "X", Schema = null });

        var cut = this.RenderComponent<CanvasView>(p => p.Add(x => x.Document, doc));

        cut.Find("[data-port-in=input]").HasAttribute("title").Should().BeFalse();
    }
}
