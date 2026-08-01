// <copyright file="RunDialogTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Components;

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// ▶️ Phase 3.5 (V5) — the run dialog. It replaced a bare JSON textarea that required you to
/// already know every variable name and its shape.
/// </summary>
public sealed class RunDialogTests : TestContext
{
    private readonly ToastService toasts = new();

    public RunDialogTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(this.toasts);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static DesignerDocument DocWith(params (string Name, string Declaration)[] variables)
    {
        var doc = new DesignerDocument { Name = "wf" };
        foreach (var (name, declaration) in variables)
        {
            doc.Variables[name] = Json(declaration);
        }

        return doc;
    }

    private (IRenderedComponent<RunDialog> Cut, List<RunRequest> Started) Render(DesignerDocument doc)
    {
        var started = new List<RunRequest>();
        var cut = this.RenderComponent<RunDialog>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.OnStart, (RunRequest r) => started.Add(r)));
        return (cut, started);
    }

    [Fact]
    public void Form_IsGeneratedFromDeclaredVariables_AndPrefilled()
    {
        var doc = DocWith(
            ("orderId", """{ "name": "orderId", "type": 0, "initialValue": "A-1", "description": "which order" }"""),
            ("retries", """{ "name": "retries", "type": 1, "initialValue": 3 }"""));

        var (cut, _) = this.Render(doc);

        cut.Find("[data-testid=run-var-orderId]").GetAttribute("value").Should().Be("A-1");
        cut.Find("[data-testid=run-var-retries]").GetAttribute("value").Should().Be("3");
        cut.Markup.Should().Contain("which order", because: "the description is the field's help text");
    }

    [Fact]
    public void FormValues_SerialiseIntoInputsWithTheDeclaredType()
    {
        var doc = DocWith(("retries", """{ "name": "retries", "type": 1, "initialValue": 3 }"""));
        var (cut, started) = this.Render(doc);

        cut.Find("[data-testid=run-var-retries]").Input("7");
        cut.Find("[data-testid=run-start]").Click();

        started.Should().ContainSingle();
        started[0].Inputs["retries"].ValueKind.Should().Be(JsonValueKind.Number);
        started[0].Inputs["retries"].GetInt32().Should().Be(7);
    }

    [Fact]
    public void BlankField_IsOmittedSoTheExistingValueApplies()
    {
        // Sending "" would clobber the declared default (or a value a previous run persisted),
        // which is the opposite of what leaving a field alone should mean.
        var doc = DocWith(("orderId", """{ "name": "orderId", "type": 0, "initialValue": "A-1" }"""));
        var (cut, started) = this.Render(doc);

        cut.Find("[data-testid=run-var-orderId]").Input(string.Empty);
        cut.Find("[data-testid=run-start]").Click();

        started[0].Inputs.Should().NotContainKey("orderId");
    }

    [Fact]
    public void SecretVariable_IsAPasswordFieldAndIsNeverPrefilled()
    {
        var doc = DocWith(("apiKey", """{ "name": "apiKey", "type": 0, "isSecret": true }"""));

        var (cut, _) = this.Render(doc);

        var field = cut.Find("[data-testid=run-var-apiKey]");
        field.GetAttribute("type").Should().Be("password");
        field.GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public void JsonMode_StillAcceptsUndeclaredInputs()
    {
        var (cut, started) = this.Render(DocWith());

        cut.Find("[data-testid=run-mode-json]").Click();
        cut.Find("[data-testid=run-inputs]").Change("""{ "adhoc": 42 }""");
        cut.Find("[data-testid=run-start]").Click();

        started[0].Inputs["adhoc"].GetInt32().Should().Be(42);
    }

    [Fact]
    public void JsonMode_InvalidJson_ToastsAndDoesNotStart()
    {
        var (cut, started) = this.Render(DocWith());

        cut.Find("[data-testid=run-mode-json]").Click();
        cut.Find("[data-testid=run-inputs]").Change("{ not json");
        cut.Find("[data-testid=run-start]").Click();

        started.Should().BeEmpty();
        this.toasts.Toasts.Should().Contain(t => t.Message.Contains("valid JSON"));
    }

    [Fact]
    public void MistypedFormValue_ToastsAndDoesNotStart()
    {
        var doc = DocWith(("payload", """{ "name": "payload", "type": 8 }"""));
        var (cut, started) = this.Render(doc);

        cut.Find("[data-testid=run-var-payload]").Input("{ not json");
        cut.Find("[data-testid=run-start]").Click();

        started.Should().BeEmpty();
        this.toasts.Toasts.Should().Contain(t => t.Message.Contains("payload"));
    }

    [Fact]
    public void WriteMode_DefaultsToThisRunOnly_AndRoundTrips()
    {
        var (cut, started) = this.Render(DocWith());

        cut.Find("[data-testid=run-start]").Click();
        started[0].VariableWriteMode.Should().Be("execution");

        cut.Find("[data-testid=run-writemode]").Change("workflow");
        cut.Find("[data-testid=run-start]").Click();
        started[1].VariableWriteMode.Should().Be("workflow");
    }

    [Fact]
    public void NoDeclaredVariables_PointsAtTheJsonEscapeHatch()
    {
        var (cut, _) = this.Render(DocWith());

        cut.Find("[data-testid=run-novars]").TextContent.Should().Contain("JSON");
    }

    [Fact]
    public void Cancel_RaisesOnCancel()
    {
        var cancelled = false;
        var cut = this.RenderComponent<RunDialog>(p => p
            .Add(c => c.Document, DocWith())
            .Add(c => c.OnCancel, () => cancelled = true));

        cut.Find("[data-testid=run-cancel]").Click();

        cancelled.Should().BeTrue();
    }
}
