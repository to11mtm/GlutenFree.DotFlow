// <copyright file="SplitPreviewTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Xunit;

/// <summary>
/// 🧪 Split preview V1/V3 — specs for <see cref="SplitPreview"/> and the sample-metadata command.
/// The replay fixtures here are the CLIENT half of the drift guard — the server half runs the
/// same object/keys/restPort through <c>SplitModule</c> (<c>SplitPreviewDriftGuardTests</c>)~ 🧩.
/// </summary>
public sealed class SplitPreviewTests
{
    // Shared drift fixture: { Foo:1, Bar:"hello", Baz:3 }, keys [Foo, Bar, Qux], rest "rest".
    private const string Sample = """{ "Foo": 1, "Bar": "hello", "Baz": 3 }""";
    private static readonly string[] Keys = ["Foo", "Bar", "Qux"];

    [Fact]
    public void Compute_MatchesModuleSemantics_ForTheDriftFixture()
    {
        var result = SplitPreview.Compute(Sample, Keys, "rest");

        result.Success.Should().BeTrue();
        result.Rows.Should().HaveCount(4);
        result.Rows[0].Should().Be(new SplitPreview.Row("Foo", "1", false, false));
        result.Rows[1].ValuePreview.Should().Be("\"hello\"");
        result.Rows[2].Should().Match<SplitPreview.Row>(r => r.Port == "Qux" && r.IsMissing && r.ValuePreview == "null");
        result.Rows[3].IsRest.Should().BeTrue();
        result.Rows[3].Port.Should().Be("rest");
        result.Rows[3].ValuePreview.Should().Contain("\"Baz\": 3");
    }

    [Fact]
    public void Compute_NoRestPort_DropsRemainder()
    {
        var result = SplitPreview.Compute(Sample, ["Foo"], null);

        result.Rows.Should().ContainSingle().Which.Port.Should().Be("Foo");
    }

    [Fact]
    public void Compute_RestWithNothingRemaining_SaysSo()
    {
        var result = SplitPreview.Compute("""{ "Foo": 1 }""", ["Foo"], "rest");

        result.Rows.Last().ValuePreview.Should().Contain("nothing remains");
    }

    [Fact]
    public void Compute_NonObjectSample_Fails()
    {
        SplitPreview.Compute("[1,2]", ["Foo"], null).Success.Should().BeFalse();
        SplitPreview.Compute("not json", ["Foo"], null).Success.Should().BeFalse();
    }

    [Fact]
    public void Compute_BlankSample_GivesEmptyStateHint()
    {
        var result = SplitPreview.Compute(string.Empty, ["Foo"], null);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("sample");
    }

    [Fact]
    public void Compute_LongValues_AreTruncated()
    {
        var big = $$"""{ "Foo": "{{new string('x', 200)}}" }""";

        var result = SplitPreview.Compute(big, ["Foo"], null);

        result.Rows[0].ValuePreview.Length.Should().BeLessThanOrEqualTo(SplitPreview.MaxValueLength + 1);
        result.Rows[0].ValuePreview.Should().EndWith("…");
    }

    [Fact]
    public void ComputeFromShape_UsesUpstreamKeysWithoutValues()
    {
        var result = SplitPreview.ComputeFromShape(["statusCode", "body"], ["statusCode", "Qux"], "rest");

        result.Rows[0].Should().Match<SplitPreview.Row>(r => r.Port == "statusCode" && !r.IsMissing);
        result.Rows[1].Should().Match<SplitPreview.Row>(r => r.Port == "Qux" && r.IsMissing);
        result.Rows[2].ValuePreview.Should().Contain("body");
    }

    // ─── Config parsing + upstream shape ────────────────────────────────────────

    private static DesignerNode SplitNode(string keysJson = """["Foo","Bar"]""", string? restPort = "rest")
    {
        var node = new DesignerNode { Id = "split-1", ModuleId = "builtin.split", Name = "Split" };
        node.Properties["keys"] = JsonDocument.Parse(keysJson).RootElement.Clone();
        if (restPort is not null)
        {
            node.Properties["restPort"] = JsonDocument.Parse($"\"{restPort}\"").RootElement.Clone();
        }

        return node;
    }

    [Fact]
    public void KeysAndRestPort_ParseFromNodeProperties_IncludingJsonStringForm()
    {
        SplitPreview.KeysFor(SplitNode()).Should().Equal("Foo", "Bar");
        SplitPreview.RestPortFor(SplitNode()).Should().Be("rest");

        var stringForm = new DesignerNode { Id = "s", ModuleId = "builtin.split", Name = "s" };
        stringForm.Properties["keys"] = JsonDocument.Parse("\"[\\\"A\\\"]\"").RootElement.Clone();
        SplitPreview.KeysFor(stringForm).Should().Equal("A");
    }

    [Fact]
    public void UpstreamShapeKeys_DeriveFromMergedUpstream()
    {
        var http = new DesignerNode
        {
            Id = "http-1",
            ModuleId = "builtin.http.request",
            Name = "HTTP",
            Schema = new ModuleSchemaDto(
                new List<PortDefinitionDto> { new("input", "input", "object", null, false, null) },
                new List<PortDefinitionDto>
                {
                    new("statusCode", "statusCode", "int", null, false, null),
                    new("body", "body", "string", null, false, null),
                },
                new List<ModulePropertyDefinitionDto>()),
        };
        http.Properties[OutputShapingUx.PropertyName] = JsonDocument.Parse("\"merged\"").RootElement.Clone();

        var doc = new DesignerDocument();
        doc.Nodes.Add(http);
        doc.Nodes.Add(SplitNode());
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "http-1",
            SourcePortName = "output",
            TargetNodeId = "split-1",
            TargetPortName = "value",
        });

        SplitPreview.UpstreamShapeKeys(doc, doc.FindNode("split-1")!).Should().Equal("statusCode", "body");
    }

    [Fact]
    public void UpstreamShapeKeys_NullWhenUpstreamIsNotMerged()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m", Name = "a" });
        doc.Nodes.Add(SplitNode());
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "a", SourcePortName = "output", TargetNodeId = "split-1", TargetPortName = "value",
        });

        SplitPreview.UpstreamShapeKeys(doc, doc.FindNode("split-1")!).Should().BeNull();
    }

    // ─── V3 — sample metadata command ───────────────────────────────────────────

    [Fact]
    public void EditNodeMetadataCommand_SetsRemoves_AndUndoes()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(SplitNode());

        var set = new EditNodeMetadataCommand("split-1", SplitPreview.SampleMetadataKey, null, Sample);
        set.Do(doc);
        SplitPreview.PersistedSample(doc.FindNode("split-1")!).Should().Be(Sample);

        set.Undo(doc);
        SplitPreview.PersistedSample(doc.FindNode("split-1")!).Should().BeNull();

        set.Do(doc);
        var remove = new EditNodeMetadataCommand("split-1", SplitPreview.SampleMetadataKey, Sample, null);
        remove.Do(doc);
        SplitPreview.PersistedSample(doc.FindNode("split-1")!).Should().BeNull();
        remove.Undo(doc);
        SplitPreview.PersistedSample(doc.FindNode("split-1")!).Should().Be(Sample);
    }

    // ─── Q1b — rest-port key expansion for downstream hinting ──────────────────

    [Fact]
    public void RestPort_ExpandsIntoRemainingUpstreamKeys_ForDownstreamTokens()
    {
        var http = new DesignerNode
        {
            Id = "http-1",
            ModuleId = "builtin.http.request",
            Name = "HTTP",
            Schema = new ModuleSchemaDto(
                new List<PortDefinitionDto>(),
                new List<PortDefinitionDto>
                {
                    new("statusCode", "statusCode", "int", null, false, null),
                    new("body", "body", "string", null, false, null),
                    new("headers", "headers", "object", null, false, null),
                },
                new List<ModulePropertyDefinitionDto>()),
        };
        http.Properties[OutputShapingUx.PropertyName] = JsonDocument.Parse("\"merged\"").RootElement.Clone();

        var split = SplitNode(keysJson: """["statusCode"]""");
        var doc = new DesignerDocument();
        doc.Nodes.Add(http);
        doc.Nodes.Add(split);
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "http-1", SourcePortName = "output", TargetNodeId = "split-1", TargetPortName = "value",
        });

        var keys = InputShape.SubKeysFor(doc, split, "rest", prefix: "split-1.rest");

        keys.Select(k => k.Token).Should().BeEquivalentTo("{{split-1.rest.body}}", "{{split-1.rest.headers}}");
    }
}
