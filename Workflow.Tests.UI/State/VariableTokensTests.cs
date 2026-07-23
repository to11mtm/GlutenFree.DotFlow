// <copyright file="VariableTokensTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Round-2 G4 — Unit tests for <see cref="VariableTokens"/> (binding token catalog)~ ✨.
/// </summary>
public sealed class VariableTokensTests
{
    private static DesignerConnection Edge(string src, string port, string tgt)
        => new() { SourceNodeId = src, SourcePortName = port, TargetNodeId = tgt, TargetPortName = "input" };

    [Fact]
    public void OptionsFor_ListsVariables_AndTransitiveUpstreamOutputs()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["count"] = JsonDocument.Parse("1").RootElement.Clone();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "builtin.log", Name = "A" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "builtin.log", Name = "B" });
        doc.Nodes.Add(new DesignerNode { Id = "target", ModuleId = "builtin.log", Name = "T" });
        doc.Nodes.Add(new DesignerNode { Id = "sideways", ModuleId = "builtin.log", Name = "S" });
        doc.Connections.Add(Edge("a", "output", "b"));
        doc.Connections.Add(Edge("b", "output", "target"));

        var options = VariableTokens.OptionsFor(doc, "target");

        options.Should().Contain(o => o.Token == "{{Variable.count}}" && o.Category == "Variables");
        options.Should().Contain(o => o.Token == "{{a.output}}", because: "transitive upstream nodes count~ 🔗");
        options.Should().Contain(o => o.Token == "{{b.output}}");
        options.Should().NotContain(o => o.Token.Contains("sideways"), because: "unconnected nodes are not upstream~ 🚫");
        options.Should().NotContain(o => o.Token.Contains("{{target."), because: "the node itself is excluded~ 🚫");
    }

    [Fact]
    public void ContainsToken_DetectsBindings()
    {
        VariableTokens.ContainsToken("{{Variable.x}}").Should().BeTrue();
        VariableTokens.ContainsToken("prefix {{node.port}} suffix").Should().BeTrue();
        VariableTokens.ContainsToken("plain text").Should().BeFalse();
        VariableTokens.ContainsToken(null).Should().BeFalse();
    }

    [Fact]
    public void OptionsFor_CyclicGraph_Terminates()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "A" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m", Name = "B" });
        doc.Connections.Add(Edge("a", "output", "b"));
        doc.Connections.Add(Edge("b", "output", "a"));

        var options = VariableTokens.OptionsFor(doc, "b");

        options.Select(o => o.Token).Should().Contain("{{a.output}}");
    }
}
