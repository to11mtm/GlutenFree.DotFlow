// <copyright file="ContextMenuTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.b.1 — Context menu + duplicate/rename command specs~ ✨.
/// </summary>
public sealed class ContextMenuTests : TestContext
{
    [Fact]
    public void ContextMenu_RendersItems_AndInvokesAction()
    {
        var invoked = false;
        var items = new List<ContextMenu.MenuItem>
        {
            new("delete", "Delete", () => { invoked = true; return Task.CompletedTask; }, Danger: true),
        };

        var cut = this.RenderComponent<ContextMenu>(p => p
            .Add(x => x.Visible, true)
            .Add(x => x.X, 10)
            .Add(x => x.Y, 20)
            .Add(x => x.Items, items));

        cut.Find("[data-testid=ctx-delete]").Click();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void ContextMenu_Hidden_WhenNotVisible()
    {
        var cut = this.RenderComponent<ContextMenu>(p => p.Add(x => x.Visible, false));

        cut.FindAll(".df-ctxmenu").Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_CreatesOffsetCopy_FreshId()
    {
        var doc = new DesignerDocument();
        var original = new DesignerNode { Id = "request-1", ModuleId = "builtin.http.request", Name = "request", X = 100, Y = 100 };
        original.Properties["method"] = System.Text.Json.JsonDocument.Parse("\"GET\"").RootElement.Clone();
        doc.Nodes.Add(original);
        var stack = new CommandStack(doc);

        // Mirror the page's duplicate logic.
        var existing = new HashSet<string> { "request-1" };
        var clone = original.Clone();
        clone.Id = NodeIdGenerator.Generate(original.ModuleId, existing);
        clone.X = original.X + 40;
        clone.Y = original.Y + 40;
        stack.Execute(new AddNodeCommand(clone));

        doc.Nodes.Should().HaveCount(2);
        var copy = doc.FindNode("request-2")!;
        copy.X.Should().Be(140);
        copy.Properties["method"].GetString().Should().Be("GET");
    }

    [Fact]
    public void Rename_AppliesViaCommand_AndUndoes()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "old" });
        var stack = new CommandStack(doc);

        stack.Execute(new RenameNodeCommand("a", "old", "new"));
        doc.FindNode("a")!.Name.Should().Be("new");

        stack.Undo();
        doc.FindNode("a")!.Name.Should().Be("old");
    }
}
