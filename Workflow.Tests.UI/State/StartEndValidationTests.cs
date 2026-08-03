// <copyright file="StartEndValidationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧪 Specs for the Start/End legibility warnings in <see cref="GraphValidator"/>~ 🚀🏁.
/// </summary>
/// <remarks>
/// Every rule here is a <b>warning</b>. The engine happily runs all of these graphs — the rules
/// exist because a Start that isn't the start is a diagram that lies, not because anything breaks.
/// Each test asserts the severity explicitly so a future change to Error can't slip through.
/// </remarks>
public sealed class StartEndValidationTests
{
    private static readonly HashSet<string> Known = new() { "m", "builtin.start", "builtin.end" };

    private static DesignerNode Node(string id, string moduleId = "m")
        => new() { Id = id, ModuleId = moduleId, Name = id };

    private static DesignerConnection Conn(string s, string t)
        => new() { SourceNodeId = s, SourcePortName = "out", TargetNodeId = t, TargetPortName = "in" };

    private static DesignerDocument Doc(
        IEnumerable<DesignerNode> nodes,
        IEnumerable<DesignerConnection>? conns = null)
    {
        var doc = new DesignerDocument();
        doc.Nodes.AddRange(nodes);
        doc.Connections.AddRange(conns ?? []);
        return doc;
    }

    [Fact]
    public void SingleStart_NoWarning()
    {
        var doc = Doc([Node("s", "builtin.start"), Node("a")], [Conn("s", "a")]);

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void MultipleStarts_Warns()
    {
        var doc = Doc([Node("s1", "builtin.start"), Node("s2", "builtin.start"), Node("a")]);

        var issues = GraphValidator.Validate(doc, Known);

        var issue = issues.Should().ContainSingle(i => i.Message.Contains("Start nodes")).Subject;
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.Message.Should().Contain("2");
    }

    [Fact]
    public void ThreeStarts_WarnsOnce()
    {
        var doc = Doc([
            Node("s1", "builtin.start"),
            Node("s2", "builtin.start"),
            Node("s3", "builtin.start"),
        ]);

        var issues = GraphValidator.Validate(doc, Known);

        issues.Count(i => i.Message.Contains("Start nodes")).Should().Be(1);
    }

    [Fact]
    public void StartWithIncomingConnection_Warns()
    {
        // Undrawable in the designer (Start has no input ports) but reachable via import~ 📥
        var doc = Doc([Node("a"), Node("s", "builtin.start")], [Conn("a", "s")]);

        var issues = GraphValidator.Validate(doc, Known);

        var issue = issues.Should().ContainSingle(i => i.Message.Contains("incoming connection")).Subject;
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.NodeId.Should().Be("s");
    }

    [Fact]
    public void EndWithOutgoingConnection_Warns()
    {
        var doc = Doc([Node("a"), Node("e", "builtin.end"), Node("b")], [Conn("a", "e"), Conn("e", "b")]);

        var issues = GraphValidator.Validate(doc, Known);

        var issue = issues.Should().ContainSingle(i => i.Message.Contains("outgoing connections")).Subject;
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.NodeId.Should().Be("e");
    }

    [Fact]
    public void TerminalEnd_NoWarning()
    {
        var doc = Doc([Node("a"), Node("e", "builtin.end")], [Conn("a", "e")]);

        var issues = GraphValidator.Validate(doc, Known);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void GraphWithNeitherModule_Unaffected()
    {
        var doc = Doc([Node("a"), Node("b")], [Conn("a", "b")]);

        GraphValidator.ValidateStartAndEnd(doc).Should().BeEmpty();
    }

    [Fact]
    public void Warnings_DoNotProduceErrors()
    {
        // D5: misuse is warned, never blocked — nothing here may reach Error severity~ 🌸
        var doc = Doc(
            [Node("s1", "builtin.start"), Node("s2", "builtin.start"), Node("e", "builtin.end"), Node("b")],
            [Conn("s1", "e"), Conn("e", "b")]);

        var issues = GraphValidator.ValidateStartAndEnd(doc);

        issues.Should().NotBeEmpty();
        issues.Should().OnlyContain(i => i.Severity == IssueSeverity.Warning);
    }
}
