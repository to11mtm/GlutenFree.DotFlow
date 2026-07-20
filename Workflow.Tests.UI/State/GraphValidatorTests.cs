// <copyright file="GraphValidatorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.3.a.2 — Specs for structural graph validation + cycle detection~ ✨.
/// </summary>
public sealed class GraphValidatorTests
{
    private static readonly HashSet<string> Known = new() { "m" };

    private static DesignerNode Node(string id) => new() { Id = id, ModuleId = "m", Name = id };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "out", TargetNodeId = t, TargetPortName = "in" };

    private static DesignerDocument Doc(IEnumerable<DesignerNode> nodes, IEnumerable<DesignerConnection> conns)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns);
        return doc;
    }

    [Fact]
    public void Validator_DetectsCycle()
    {
        var doc = Doc(new[] { Node("a"), Node("b") }, new[] { Conn("a", "b"), Conn("b", "a") });

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().Contain(i => i.Message.Contains("cycle"));
    }

    [Fact]
    public void Validator_AllowsDiamond_NotACycle()
    {
        var doc = Doc(
            new[] { Node("a"), Node("b"), Node("c"), Node("d") },
            new[] { Conn("a", "b"), Conn("a", "c"), Conn("b", "d"), Conn("c", "d") });

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().NotContain(i => i.Message.Contains("cycle"));
    }

    [Fact]
    public void Validator_UnknownModule_Flagged()
    {
        var doc = Doc(new[] { new DesignerNode { Id = "x", ModuleId = "nope", Name = "X" } }, System.Array.Empty<DesignerConnection>());

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().Contain(i => i.Message.Contains("Unknown module") && i.NodeId == "x");
    }

    [Fact]
    public void Validator_DanglingConnection_Flagged()
    {
        var doc = Doc(new[] { Node("a") }, new[] { Conn("a", "ghost") });

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().Contain(i => i.Message.Contains("missing target node"));
    }

    [Fact]
    public void Validator_DuplicateConnection_Flagged()
    {
        var doc = Doc(new[] { Node("a"), Node("b") }, new[] { Conn("a", "b"), Conn("a", "b") });

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().Contain(i => i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validator_SelfConnection_Flagged()
    {
        var doc = Doc(new[] { Node("a") }, new[] { Conn("a", "a") });

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().Contain(i => i.Message.Contains("cannot connect to itself"));
    }

    [Fact]
    public void WouldCreateCycle_DetectsBackEdge()
    {
        var doc = Doc(new[] { Node("a"), Node("b") }, new[] { Conn("a", "b") });

        GraphValidator.WouldCreateCycle(doc, "b", "a").Should().BeTrue();
        GraphValidator.WouldCreateCycle(doc, "a", "b").Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_DiamondClose_IsNotCycle()
    {
        // a→b, a→c, b→d ; adding c→d must NOT be a cycle.
        var doc = Doc(
            new[] { Node("a"), Node("b"), Node("c"), Node("d") },
            new[] { Conn("a", "b"), Conn("a", "c"), Conn("b", "d") });

        GraphValidator.WouldCreateCycle(doc, "c", "d").Should().BeFalse();
    }
}
