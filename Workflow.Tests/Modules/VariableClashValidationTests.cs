// <copyright file="VariableClashValidationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Validation;
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// §10.2 — the save-time validator warns when <c>{{Variable.x}}</c> sits in a node whose input port
/// <c>x</c> is wired (the token reads the variable, not the port), honouring the designer's
/// "keep the variable" acknowledgement.
/// </summary>
public sealed class VariableClashValidationTests
{
    private static ModuleAwareWorkflowValidator Validator()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new OutputModule("vc.src", new Dictionary<string, object?> { ["value"] = 7 }));
        registry.RegisterModule(new CaptureModule("vc.sink", inputPorts: new[] { "count" }, templatedProperties: new[] { "p" }));
        registry.RegisterModule(new CaptureModule("vc.plain", inputPorts: new[] { "count" }));
        return new ModuleAwareWorkflowValidator(registry);
    }

    private static WorkflowDefinition Workflow(
        string template,
        bool declareCount,
        string sinkModule = "vc.sink",
        string? ack = null,
        string property = "p")
        => FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "vc.src"),
                FindingDefs.Node(
                    "sink",
                    sinkModule,
                    properties: new (string, object?)[] { (property, template) },
                    metadata: ack is null ? null : HashMap<string, string>.Empty.Add(VariableClashChecker.AckKey, ack)),
            },
            connections: new[] { FindingDefs.Edge("src", "value", "sink", "count") },
            variables: declareCount ? new[] { new VariableDefinition("count", PropertyType.Int, FindingDefs.Json(99)) } : null);

    private static IReadOnlyList<ValidationWarning> Clashes(WorkflowDefinition workflow)
        => Validator().Validate(workflow).Warnings.Where(w => w.Code == VariableClashChecker.Code).ToList();

    [Fact]
    public void DeclaredVariable_ClashingWithAWiredPort_Warns()
    {
        var warnings = Clashes(Workflow("{{Variable.count}}", declareCount: true));

        warnings.Should().ContainSingle().Which.NodeId.Should().Be("sink");
        warnings[0].Message.Should().Contain("reads the variable 'count'").And.Contain("{{src.value}}");
    }

    [Fact]
    public void UndeclaredVariable_ClashingWithAWiredPort_WarnsThatTheRunWillFail()
    {
        Clashes(Workflow("{{Variable.count > 5}}", declareCount: false))
            .Should().ContainSingle().Which.Message.Should().Contain("will fail at run time").And.Contain("{{src.value}}");
    }

    [Fact]
    public void Acknowledged_IsQuiet_ButARewireRaisesItAgain()
    {
        Clashes(Workflow("{{Variable.count}}", declareCount: true, ack: "other@x.y, count@src.value")).Should().BeEmpty();
        Clashes(Workflow("{{Variable.count}}", declareCount: true, ack: "count@elsewhere.value")).Should().ContainSingle();
    }

    [Fact]
    public void NonTemplatedProperties_AndUnwiredNames_AreIgnored()
    {
        Clashes(Workflow("{{Variable.count}}", declareCount: true, sinkModule: "vc.plain")).Should().BeEmpty();
        Clashes(Workflow("{{Variable.total}}", declareCount: true)).Should().BeEmpty();
        Clashes(Workflow(@"\{{Variable.count}}", declareCount: true)).Should().BeEmpty(because: "an escaped token is literal text");
    }
}
