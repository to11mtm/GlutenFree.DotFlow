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

    // ── 💼 Transaction bodies (L12) ───────────────────────────────────────────────────────

    private static DesignerNode DbNode(string id, string moduleId, string? connectionId = null)
    {
        var n = new DesignerNode { Id = id, ModuleId = moduleId, Name = id };
        if (connectionId is not null)
        {
            n.Properties["connectionId"] = System.Text.Json.JsonDocument.Parse($"\"{connectionId}\"").RootElement.Clone();
        }

        return n;
    }

    private static DesignerConnection BodyConn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "transactionBody", TargetNodeId = t, TargetPortName = "input" };

    [Fact]
    public void Transactions_NestedTransaction_IsABlockingError()
    {
        var doc = Doc(
            new[]
            {
                DbNode("tx1", "builtin.database.transaction", "db"),
                DbNode("tx2", "builtin.database.transaction", "db"),
            },
            new[] { BodyConn("tx1", "tx2") });

        var issues = GraphValidator.ValidateTransactions(doc);

        issues.Should().ContainSingle(i =>
            i.Severity == IssueSeverity.Error && i.NodeId == "tx2" && i.Message.Contains("Nested transactions"));
    }

    [Fact]
    public void Transactions_BodyNodeOnAnotherConnection_Warns()
    {
        var doc = Doc(
            new[]
            {
                DbNode("tx", "builtin.database.transaction", "orders"),
                DbNode("exec", "builtin.database.execute", "analytics"),
            },
            new[] { BodyConn("tx", "exec") });

        var issues = GraphValidator.ValidateTransactions(doc);

        issues.Should().ContainSingle(i =>
            i.Severity == IssueSeverity.Warning && i.NodeId == "exec" && i.Message.Contains("outside the transaction"));
    }

    [Fact]
    public void Transactions_MatchingConnection_IsClean()
    {
        var doc = Doc(
            new[]
            {
                DbNode("tx", "builtin.database.transaction", "orders"),
                DbNode("exec", "builtin.database.execute", "orders"),
                DbNode("log", "builtin.log"),
            },
            new[] { BodyConn("tx", "exec"), Conn("exec", "log") });

        GraphValidator.ValidateTransactions(doc).Should().BeEmpty();
    }

    [Fact]
    public void Transactions_DeepBodyNode_IsStillChecked()
    {
        // The whole downstream closure of transactionBody is inside the transaction~
        var doc = Doc(
            new[]
            {
                DbNode("tx", "builtin.database.transaction", "orders"),
                DbNode("first", "builtin.database.execute", "orders"),
                DbNode("second", "builtin.database.execute", "other"),
            },
            new[] { BodyConn("tx", "first"), Conn("first", "second") });

        GraphValidator.ValidateTransactions(doc).Should().ContainSingle(i => i.NodeId == "second");
    }
}