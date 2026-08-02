// <copyright file="ExportImportUiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 📤📥 Phase 3.6 (E1/E6) — the export dialog: only speaks up when it has something to say, and
/// never lets a credential leave silently.
/// </summary>
public sealed class ExportImportUiTests : TestContext
{
    public ExportImportUiTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static JsonElement Str(string value) => JsonSerializer.SerializeToElement(value);

    private static WorkflowDto Workflow(Dictionary<string, JsonElement>? properties = null)
        => new(
            Guid.NewGuid(), "Order sync", null, "1.2.0",
            new List<NodeDto>
            {
                new("n1", "builtin.http.request", "Fetch", properties ?? new Dictionary<string, JsonElement>(), new PositionDto(0, 0)),
            },
            new List<ConnectionDto>(), new Dictionary<string, JsonElement>(), null, null, null, null, null);

    private (IRenderedComponent<ExportDialog> Cut, List<WorkflowDto> Confirmed) Render(WorkflowDto workflow, bool dirty = false)
    {
        var confirmed = new List<WorkflowDto>();
        var cut = this.RenderComponent<ExportDialog>(p => p
            .Add(c => c.Workflow, workflow)
            .Add(c => c.IsDirty, dirty)
            .Add(c => c.OnConfirm, (WorkflowDto w) => confirmed.Add(w)));
        return (cut, confirmed);
    }

    [Fact]
    public void ShowsTheFilename()
    {
        var (cut, _) = this.Render(Workflow());

        cut.Find("[data-testid=export-filename]").TextContent.Should().Contain("order-sync-1-2-0.dotflow.json");
    }

    [Fact]
    public void NotesUnsavedChanges_BecauseExportUsesTheInMemoryDocument()
    {
        // Q4 — export is WYSIWYG, so say so when the canvas is ahead of the saved copy.
        var (cut, _) = this.Render(Workflow(), dirty: true);

        cut.Find("[data-testid=export-dirty-note]").TextContent.Should().Contain("unsaved changes");
    }

    [Fact]
    public void CleanWorkflow_ShowsNoCredentialWarning()
    {
        var (cut, _) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["url"] = Str("https://example.com") }));

        cut.FindAll("[data-testid=export-credential-warning]").Should().BeEmpty();
        cut.FindAll("[data-testid=export-redact]").Should().BeEmpty();
    }

    [Fact]
    public void CredentialShapedProperty_WarnsAndOffersRedaction()
    {
        var (cut, _) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["apiKey"] = Str("sk-live-123") }));

        cut.Find("[data-testid=export-credential-warning]").TextContent.Should().Contain("apiKey");
        cut.Find("[data-testid=export-redact]").Should().NotBeNull();
    }

    [Fact]
    public void RedactIsOnByDefault_SoTheSafeChoiceIsTheDefaultChoice()
    {
        var (cut, confirmed) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["apiKey"] = Str("sk-live-123") }));

        cut.Find("[data-testid=export-confirm]").Click();

        confirmed.Should().ContainSingle();
        confirmed[0].Nodes[0].Properties["apiKey"].GetString().Should().BeEmpty();
    }

    [Fact]
    public void OptingOutOfRedaction_ExportsTheRealValue()
    {
        // The user is allowed to export a credential — deliberately, and having been told.
        var (cut, confirmed) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["apiKey"] = Str("sk-live-123") }));

        cut.Find("[data-testid=export-redact]").Change(false);
        cut.Find("[data-testid=export-confirm]").Click();

        confirmed[0].Nodes[0].Properties["apiKey"].GetString().Should().Be("sk-live-123");
    }

    [Fact]
    public void BoundCredential_IsNotFlagged()
    {
        // {{Variable.apiKey}} is the pattern we *want* — flagging it would train people to click
        // through the warning.
        var (cut, _) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["apiKey"] = Str("{{Variable.apiKey}}") }));

        cut.FindAll("[data-testid=export-credential-warning]").Should().BeEmpty();
    }

    [Fact]
    public void Cancel_RaisesOnCancel()
    {
        var cancelled = false;
        var cut = this.RenderComponent<ExportDialog>(p => p
            .Add(c => c.Workflow, Workflow())
            .Add(c => c.OnCancel, () => cancelled = true));

        cut.Find("[data-testid=export-cancel]").Click();

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void ExportedFile_IsReadableBackByImport()
    {
        // The round-trip that matters end-to-end: whatever the dialog hands to the downloader must
        // be something Import can open.
        var (cut, confirmed) = this.Render(Workflow(new Dictionary<string, JsonElement> { ["url"] = Str("https://example.com") }));
        cut.Find("[data-testid=export-confirm]").Click();

        var result = WorkflowExport.Read(WorkflowExport.Write(confirmed[0]));

        result.Success.Should().BeTrue();
        result.Workflow!.Nodes.Should().ContainSingle();
        result.Workflow.Nodes[0].Properties["url"].GetString().Should().Be("https://example.com");
    }
}
