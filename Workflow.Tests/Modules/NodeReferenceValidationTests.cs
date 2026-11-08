// <copyright file="NodeReferenceValidationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Validation;
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// §10.1 — the save-time validator warns when <c>{{nodeId.port}}</c> names a node that isn't upstream
/// of the referencing node; ancestors (any connection path in) are fine.
/// </summary>
public sealed class NodeReferenceValidationTests
{
    private static IReadOnlyList<ValidationWarning> Warnings(string template, params (string From, string To)[] edges)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new PassModule("nr.pass"));
        registry.RegisterModule(new CaptureModule("nr.sink", templatedProperties: new[] { "p" }));
        var workflow = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("a", "nr.pass"),
                FindingDefs.Node("b", "nr.pass"),
                FindingDefs.Node("c", "nr.pass"),
                FindingDefs.Node("sink", "nr.sink", properties: new (string, object?)[] { ("p", template) }),
            },
            connections: edges.Select(e => FindingDefs.Edge(e.From, "output", e.To, "input")).ToArray());
        return new ModuleAwareWorkflowValidator(registry).Validate(workflow).Warnings
            .Where(w => w.Code == NodeReferenceChecker.Code).ToList();
    }

    [Fact]
    public void Ancestors_AnyNumberOfHopsBack_AreFine()
    {
        Warnings("{{a.output}} {{b.output.x}} {{c.output}}", ("a", "b"), ("b", "c"), ("c", "sink")).Should().BeEmpty();
    }

    [Fact]
    public void ASiblingBranch_IsNotUpstream_AndWarns()
    {
        var warnings = Warnings("{{b.output}}", ("a", "b"), ("a", "sink"));

        warnings.Should().ContainSingle().Which.NodeId.Should().Be("sink");
        warnings[0].Message.Should().Contain("reads node 'b', which isn't upstream of this node");
    }

    [Fact]
    public void ReservedRoots_UnknownNodes_AndEscapedTokens_AreLeftToOtherChecks()
    {
        Warnings("{{Variable.x}} {{input.y}} {{item.z}} {{ghost.output}} \\{{b.output}}", ("a", "sink")).Should().BeEmpty();
    }
}
