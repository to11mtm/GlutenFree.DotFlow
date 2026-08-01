// <copyright file="VariableLintTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧭 Phase 3.5 (V7) — design-time linting of <c>{{…}}</c> bindings. Since V4 an unresolvable
/// reference fails the node, so these are the errors that would otherwise only appear mid-run.
/// </summary>
public class VariableLintTests
{
    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    /// <summary>A realistic declaration — a bare JSON string would trip the unassigned warning.</summary>
    private static JsonElement Decl(string name, string? initialValueJson = null)
        => JsonDocument.Parse(initialValueJson is null
            ? $$"""{ "name": "{{name}}", "type": 0 }"""
            : $$"""{ "name": "{{name}}", "type": 0, "initialValue": {{initialValueJson}} }""").RootElement.Clone();

    private static ModuleSchemaDto SchemaWith(params string[] templatedProperties)
        => new(
            new List<PortDefinitionDto>(),
            new List<PortDefinitionDto>(),
            templatedProperties
                .Select(p => new ModulePropertyDefinitionDto(p, p, "String", null, false, null, "Text", null, true))
                .ToList());

    private static DesignerDocument DocWithNode(string property, string value, string nodeId = "n1")
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode
        {
            Id = nodeId,
            ModuleId = "builtin.http.request",
            Name = "Fetch",
            Schema = SchemaWith(property),
            Properties = { [property] = Str(value) },
        });
        return doc;
    }

    private static DesignerNode SetVariableNode(string id, string variableName)
        => new()
        {
            Id = id,
            ModuleId = VariableLint.SetVariableModuleId,
            Name = $"Set {variableName}",
            Properties = { ["name"] = Str(variableName) },
        };

    [Fact]
    public void UnknownVariable_IsAnError()
    {
        var doc = DocWithNode("url", "{{Variable.typo}}/orders");

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Error);
        issues[0].Message.Should().Contain("typo").And.Contain("fail at run time");
        issues[0].NodeId.Should().Be("n1");
    }

    [Fact]
    public void DeclaredVariable_IsClean()
    {
        var doc = DocWithNode("url", "{{Variable.host}}/orders");
        doc.Variables["host"] = Decl("host", "\"https://example.com\"");

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void KnownGlobal_IsClean()
    {
        var doc = DocWithNode("url", "{{Variable.apiBaseUrl}}/orders");

        VariableLint.Validate(doc, new[] { "apiBaseUrl" })
            .Should().NotContain(i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void UpstreamSetVariable_SuppressesTheError()
    {
        var doc = DocWithNode("url", "{{Variable.token}}/orders");
        doc.Nodes.Add(SetVariableNode("setter", "token"));
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "setter", SourcePortName = "output", TargetNodeId = "n1", TargetPortName = "input",
        });

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void SetVariableThatIsNotUpstream_WarnsInsteadOfErroring()
    {
        // It might run first, it might not — the graph doesn't say. Blocking would be wrong;
        // silence would be worse.
        var doc = DocWithNode("url", "{{Variable.token}}/orders");
        doc.Nodes.Add(SetVariableNode("setter", "token"));

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Warning);
        issues[0].Message.Should().Contain("isn't upstream");
    }

    [Fact]
    public void UnknownNodeReference_IsAnError_AndSuggestsTheEscape()
    {
        // The Mustache-template case: a value read from a database, pasted into a templated field.
        var doc = DocWithNode("url", "Dear {{customer.name}}");

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Error);
        issues[0].Message.Should().Contain("customer").And.Contain(@"\{\{");
    }

    [Fact]
    public void KnownNodeReference_IsClean()
    {
        var doc = DocWithNode("url", "{{up1.output}}");
        doc.Nodes.Add(new DesignerNode { Id = "up1", ModuleId = "m", Name = "Up" });

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void EscapedBraces_AreNotLinted()
    {
        var doc = DocWithNode("url", @"Hello \{\{customer.name}}");

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void PropertiesThatDoNotExpandTemplates_AreNotLinted()
    {
        // SQL text legitimately contains braces and is never expanded — linting it would be noise.
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode
        {
            Id = "n1",
            ModuleId = "builtin.database.query",
            Name = "Query",
            Schema = SchemaWith("url"),
            Properties = { ["query"] = Str("SELECT {{Variable.nope}}") },
        });

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void NodeWithoutASchema_IsNotLinted()
    {
        // Without a schema we can't know what expands — inventing errors would be worse than
        // staying quiet (the unknown-module error already covers this case).
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode
        {
            Id = "n1",
            ModuleId = "unknown",
            Name = "N",
            Properties = { ["url"] = Str("{{Variable.nope}}") },
        });

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void VariableInsideAnExpression_IsChecked()
    {
        var doc = DocWithNode("url", "{{Variable.count > 5}}");

        VariableLint.Validate(doc).Should().ContainSingle(i => i.Message.Contains("count"));
    }

    [Fact]
    public void BuiltInsInsideAnExpression_AreNotTreatedAsNodeReferences()
    {
        // The binder leaves Math.max/JSON.parse for the evaluator; the lint must do the same or
        // every expression becomes a false error.
        var doc = DocWithNode("url", "{{Math.max(1, 2)}}");

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void NestedVariablePath_ChecksOnlyTheRootName()
    {
        var doc = DocWithNode("url", "{{Variable.user.name}}");
        doc.Variables["user"] = Decl("user", "\"x\"");

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void DeclaredButUnassigned_Warns()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["orderId"] = JsonDocument.Parse("""{ "name": "orderId", "type": 0 }""").RootElement.Clone();

        var issues = VariableLint.Validate(doc);

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Warning);
        issues[0].Message.Should().Contain("starts as null").And.Contain("run input");
    }

    [Fact]
    public void DeclaredWithAnInitialValue_DoesNotWarn()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["orderId"] = JsonDocument.Parse(
            """{ "name": "orderId", "type": 0, "initialValue": "A-1" }""").RootElement.Clone();

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void DeclaredSecret_DoesNotWarn_BecauseItsValueComesFromElsewhereByDesign()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Variables["apiKey"] = JsonDocument.Parse(
            """{ "name": "apiKey", "type": 0, "isSecret": true }""").RootElement.Clone();

        VariableLint.Validate(doc).Should().BeEmpty();
    }

    [Fact]
    public void HasUnresolvableReference_DrivesTheEditorBadge()
    {
        var doc = DocWithNode("url", "{{Variable.typo}}");
        var node = doc.FindNode("n1")!;

        VariableLint.HasUnresolvableReference(doc, node, "url", "{{Variable.typo}}").Should().BeTrue();
        VariableLint.HasUnresolvableReference(doc, node, "url", "{{Variable.typo}}", new[] { "typo" }).Should().BeFalse();
        VariableLint.HasUnresolvableReference(doc, node, "url", "no tokens here").Should().BeFalse();
    }
}
