// <copyright file="ImportReportTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🧭 Phase 3.6 (E5) — the portability report. Without it, importing a workflow that references a
/// module this environment lacks produces a bare 422 from the API with no explanation, and a
/// workflow with an unavailable *pinned* version saves cleanly then fails at execution.
/// </summary>
public class ImportReportTests
{
    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    private static NodeDto Node(
        string id,
        string moduleId,
        string name,
        Dictionary<string, JsonElement>? properties = null,
        Dictionary<string, string>? metadata = null)
        => new(id, moduleId, name, properties ?? new Dictionary<string, JsonElement>(), new PositionDto(0, 0), null, null, null, metadata);

    private static WorkflowDto Workflow(
        IEnumerable<NodeDto>? nodes = null,
        Dictionary<string, JsonElement>? variables = null)
        => new(
            Guid.NewGuid(), "wf", null, "1.0.0",
            (nodes ?? Enumerable.Empty<NodeDto>()).ToList(),
            new List<ConnectionDto>(),
            variables,
            null, null, null, null, null);

    private static ISet<string> Known(params string[] ids) => new HashSet<string>(ids, StringComparer.Ordinal);

    [Fact]
    public void MissingModule_IsAnError_AndNamesTheModule()
    {
        var workflow = Workflow(new[] { Node("n1", "acme.custom", "Custom step") });

        var issues = ImportReport.Build(workflow, Known("builtin.http.request"));

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Error);
        issues[0].Message.Should().Contain("acme.custom").And.Contain("isn't installed");
        ImportReport.HasBlockers(issues).Should().BeTrue();
    }

    [Fact]
    public void InstalledModule_IsClean()
    {
        var workflow = Workflow(new[] { Node("n1", "builtin.http.request", "Fetch") });

        var issues = ImportReport.Build(workflow, Known("builtin.http.request"));

        issues.Should().BeEmpty();
        ImportReport.HasBlockers(issues).Should().BeFalse();
    }

    [Fact]
    public void PinnedVersionUnavailable_WarnsRatherThanBlocks()
    {
        // It imports and saves fine — and fails at execution. Pull that forward.
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.http.request", "Fetch", metadata: new Dictionary<string, string> { ["moduleVersion"] = "2.0.0" }),
        });

        var issues = ImportReport.Build(
            workflow,
            Known("builtin.http.request"),
            new Dictionary<string, IReadOnlyCollection<string>> { ["builtin.http.request"] = new[] { "1.0.0" } });

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Warning);
        issues[0].Message.Should().Contain("2.0.0").And.Contain("fail when the workflow runs");
    }

    [Fact]
    public void PinnedVersionAvailable_IsClean()
    {
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.http.request", "Fetch", metadata: new Dictionary<string, string> { ["moduleVersion"] = "1.0.0" }),
        });

        var issues = ImportReport.Build(
            workflow,
            Known("builtin.http.request"),
            new Dictionary<string, IReadOnlyCollection<string>> { ["builtin.http.request"] = new[] { "1.0.0" } });

        issues.Should().BeEmpty();
    }

    [Fact]
    public void UnknownConnectionId_Warns()
    {
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.database.query", "Query",
                new Dictionary<string, JsonElement> { ["connectionId"] = Str("OrdersDb") }),
        });

        var issues = ImportReport.Build(
            workflow, Known("builtin.database.query"), null, knownConnectionIds: new[] { "OtherDb" });

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Warning);
        issues[0].Message.Should().Contain("OrdersDb");
    }

    [Fact]
    public void ReferencedGlobalNotPresentHere_Warns()
    {
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.http.request", "Fetch",
                new Dictionary<string, JsonElement> { ["url"] = Str("{{Variable.apiBaseUrl}}/orders") }),
        });

        var issues = ImportReport.Build(
            workflow, Known("builtin.http.request"), null, null, globalVariableNames: Array.Empty<string>());

        issues.Should().ContainSingle(i => i.Message.Contains("apiBaseUrl"));
    }

    [Fact]
    public void ReferencedGlobalPresentHere_IsClean()
    {
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.http.request", "Fetch",
                new Dictionary<string, JsonElement> { ["url"] = Str("{{Variable.apiBaseUrl}}/orders") }),
        });

        var issues = ImportReport.Build(
            workflow, Known("builtin.http.request"), null, null, globalVariableNames: new[] { "apiBaseUrl" });

        issues.Should().BeEmpty();
    }

    [Fact]
    public void VariableDeclaredByTheWorkflow_IsNotReportedAsMissing()
    {
        var workflow = Workflow(
            new[]
            {
                Node("n1", "builtin.http.request", "Fetch",
                    new Dictionary<string, JsonElement> { ["url"] = Str("{{Variable.host}}/orders") }),
            },
            new Dictionary<string, JsonElement>
            {
                ["host"] = JsonDocument.Parse("""{ "name": "host", "type": "String" }""").RootElement.Clone(),
            });

        var issues = ImportReport.Build(
            workflow, Known("builtin.http.request"), null, null, globalVariableNames: Array.Empty<string>());

        issues.Should().BeEmpty();
    }

    [Fact]
    public void UnknownGlobals_AreNotReportedWhenWeCouldntCheck()
    {
        // null globals means "the store was unreachable", not "no globals exist" — inventing
        // warnings from a failed lookup would be worse than staying quiet.
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.http.request", "Fetch",
                new Dictionary<string, JsonElement> { ["url"] = Str("{{Variable.whatever}}") }),
        });

        var issues = ImportReport.Build(workflow, Known("builtin.http.request"), null, null, globalVariableNames: null);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void SecretVariables_AreCalledOut()
    {
        var workflow = Workflow(
            null,
            new Dictionary<string, JsonElement>
            {
                ["apiKey"] = JsonDocument.Parse("""{ "name": "apiKey", "type": "String", "isSecret": true }""").RootElement.Clone(),
            });

        var issues = ImportReport.Build(workflow, Known());

        issues.Should().ContainSingle(i => i.Message.Contains("apiKey") && i.Message.Contains("no value by design"));
    }

    [Fact]
    public void ErrorsSortBeforeWarnings()
    {
        var workflow = Workflow(new[]
        {
            Node("n1", "builtin.database.query", "Query",
                new Dictionary<string, JsonElement> { ["connectionId"] = Str("OrdersDb") }),
            Node("n2", "acme.missing", "Custom"),
        });

        var issues = ImportReport.Build(
            workflow, Known("builtin.database.query"), null, knownConnectionIds: Array.Empty<string>());

        issues.Should().HaveCount(2);
        issues[0].Severity.Should().Be(IssueSeverity.Error);
        issues[1].Severity.Should().Be(IssueSeverity.Warning);
    }
}
