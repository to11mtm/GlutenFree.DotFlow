// <copyright file="WorkflowExportTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 📦 Phase 3.6 (E1/E3/E4/E6) — the export file format: envelope, readability, round-trip, and the
/// credential guard.
/// </summary>
public class WorkflowExportTests
{
    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    private static WorkflowDto Sample(params NodeDto[] nodes)
        => new(
            Guid.NewGuid(),
            "Order sync",
            "syncs orders",
            "1.2.0",
            nodes.Length > 0 ? new List<NodeDto>(nodes) : new List<NodeDto>
            {
                new("http-1", "builtin.http.request", "Fetch", new Dictionary<string, JsonElement>(), new PositionDto(120, 340)),
            },
            new List<ConnectionDto>(),
            new Dictionary<string, JsonElement>(),
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new List<string> { "nightly" });

    [Fact]
    public void Write_WrapsInAVersionedEnvelope()
    {
        var json = WorkflowExport.Write(Sample(), engineVersion: "1.4.0");
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("dotflowFormat").GetInt32().Should().Be(WorkflowExport.CurrentFormat);
        root.GetProperty("engineVersion").GetString().Should().Be("1.4.0");
        root.TryGetProperty("workflow", out _).Should().BeTrue();
    }

    [Fact]
    public void Write_IsIndented_BecauseReadabilityIsThePoint()
        => WorkflowExport.Write(Sample()).Should().Contain("\n  ");

    [Fact]
    public void RoundTrip_PreservesTheWorkflow()
    {
        var original = Sample();

        var result = WorkflowExport.Read(WorkflowExport.Write(original));

        result.Success.Should().BeTrue();
        result.Workflow!.Name.Should().Be(original.Name);
        result.Workflow.Version.Should().Be(original.Version);
        result.Workflow.Tags.Should().BeEquivalentTo(original.Tags);
    }

    [Fact]
    public void RoundTrip_PreservesNodeLayout()
    {
        // The feedback's "maintain the layout" requirement, pinned.
        var result = WorkflowExport.Read(WorkflowExport.Write(Sample()));

        var node = result.Workflow!.Nodes[0];
        node.Position!.X.Should().Be(120);
        node.Position.Y.Should().Be(340);
    }

    [Fact]
    public void RoundTrip_KeepsIdAndTimestamps()
    {
        // Q5 — these travel, because they carry the workflow's history and the id is what makes
        // overwrite-if-existing possible.
        var original = Sample();

        var restored = WorkflowExport.Read(WorkflowExport.Write(original)).Workflow!;

        restored.Id.Should().Be(original.Id);
        restored.CreatedAt.Should().BeCloseTo(original.CreatedAt!.Value, TimeSpan.FromSeconds(1));
        restored.UpdatedAt.Should().BeCloseTo(original.UpdatedAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Read_AcceptsABareWorkflowDtoAsFormatZero_WithAWarning()
    {
        // Q6b — people paste API responses and hand-craft files; refusing them buys nothing.
        var bare = JsonSerializer.Serialize(Sample(), WorkflowExport.Options);

        var result = WorkflowExport.Read(bare);

        result.Success.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("no DotFlow envelope");
    }

    [Fact]
    public void Read_RefusesANewerFormat_WithAnActionableMessage()
    {
        var future = $$"""{ "dotflowFormat": {{WorkflowExport.CurrentFormat + 1}}, "workflow": {} }""";

        var result = WorkflowExport.Read(future);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Upgrade DotFlow");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{ "dotflowFormat": 1 }""")]
    public void Read_FailsLegiblyOnRubbish(string input)
    {
        var result = WorkflowExport.Read(input);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Read_RejectsJsonThatIsntAWorkflow()
        => WorkflowExport.Read("""{ "hello": "world" }""").Success.Should().BeFalse();

    [Fact]
    public void Read_ToleratesMissingCollections()
    {
        // A hand-edited file with no "nodes" key should produce a usable document, not an NRE later.
        var minimal = """{ "dotflowFormat": 1, "workflow": { "name": "wf", "version": "1.0.0" } }""";

        var result = WorkflowExport.Read(minimal);

        result.Success.Should().BeTrue();
        result.Workflow!.Nodes.Should().BeEmpty();
        result.Workflow.Connections.Should().BeEmpty();
    }

    [Fact]
    public void VariableEnums_ExportAsNames_NotOrdinals()
    {
        // E3 / F4 — `"type": 0` tells a reviewer nothing, and made enum ordinals a load-bearing
        // contract. The designer's own serializer writes names now.
        var variable = new WorkflowVariable(
            "orderId", VariableValueType.String, null, null, VariableSeed.SeedOnly, false);

        var json = WorkflowVariables.ToJson(variable);

        json.GetProperty("type").GetString().Should().Be("String");
        json.GetProperty("seed").GetString().Should().Be("SeedOnly");
    }

    [Fact]
    public void VariableEnums_StillParseFromOrdinals()
    {
        // Definitions written before E3 must keep loading.
        var legacy = JsonDocument.Parse("""{ "name": "c", "type": 1, "seed": 1 }""").RootElement;

        var parsed = WorkflowVariables.Parse("c", legacy);

        parsed.Type.Should().Be(VariableValueType.Int);
        parsed.Seed.Should().Be(VariableSeed.AlwaysOverride);
    }

    [Theory]
    [InlineData("Order sync", "1.2.0", "order-sync-1-2-0.dotflow.json")]
    [InlineData("  weird///name  ", "1.0.0", "weird-name-1-0-0.dotflow.json")]
    [InlineData(null, null, "workflow.dotflow.json")]
    public void FileNameFor_ProducesASafeSlug(string? name, string? version, string expected)
        => WorkflowExport.FileNameFor(name, version).Should().Be(expected);

    // ── E6: credential guard ────────────────────────────────────────────────

    private static NodeDto NodeWith(string name, params (string Key, string Value)[] properties)
        => new(
            "n1",
            "builtin.http.request",
            name,
            properties.ToDictionary(p => p.Key, p => Str(p.Value)),
            new PositionDto(0, 0));

    [Fact]
    public void FindCredentialProperties_FlagsCredentialShapedNames()
    {
        var workflow = Sample(NodeWith("Fetch",
            ("url", "https://example.com"),
            ("apiKey", "sk-live-123"),
            ("bearerToken", "abc"),
            ("password", "hunter2")));

        var found = WorkflowExport.FindCredentialProperties(workflow);

        found.Select(f => f.PropertyName).Should().BeEquivalentTo("apiKey", "bearerToken", "password");
    }

    [Fact]
    public void FindCredentialProperties_IgnoresBindings()
    {
        // {{Variable.apiKey}} is the *safe* pattern — flagging it would train people to ignore
        // the warning.
        var workflow = Sample(NodeWith("Fetch", ("apiKey", "{{Variable.apiKey}}")));

        WorkflowExport.FindCredentialProperties(workflow).Should().BeEmpty();
    }

    [Fact]
    public void FindCredentialProperties_IgnoresEmptyValues()
        => WorkflowExport.FindCredentialProperties(Sample(NodeWith("Fetch", ("apiKey", "")))).Should().BeEmpty();

    [Fact]
    public void Redact_BlanksFlaggedValuesAndLeavesTheRest()
    {
        var workflow = Sample(NodeWith("Fetch",
            ("url", "https://example.com"),
            ("apiKey", "sk-live-123")));

        var redacted = WorkflowExport.Redact(workflow);

        redacted.Nodes[0].Properties["apiKey"].GetString().Should().BeEmpty();
        redacted.Nodes[0].Properties["url"].GetString().Should().Be("https://example.com");
    }

    [Fact]
    public void Redact_DoesNotMutateTheOriginal()
    {
        var workflow = Sample(NodeWith("Fetch", ("apiKey", "sk-live-123")));

        WorkflowExport.Redact(workflow);

        workflow.Nodes[0].Properties["apiKey"].GetString().Should().Be("sk-live-123");
    }

    [Fact]
    public void RedactedWorkflow_HasNoCredentialInTheExportedBytes()
    {
        var workflow = Sample(NodeWith("Fetch", ("apiKey", "sk-live-SECRET")));

        var json = WorkflowExport.Write(WorkflowExport.Redact(workflow));

        json.Should().NotContain("sk-live-SECRET");
    }
}
