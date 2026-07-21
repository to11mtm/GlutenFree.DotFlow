// <copyright file="TestRunnerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Scripts.Components;
using Workflow.UI.Client.Services;
using Xunit;

/// <summary>
/// 🧪 Phase 3.4.3 — bUnit tests for the inline test runner panel~ ✨.
/// </summary>
public sealed class TestRunnerTests : TestContext
{
    private readonly FakeLocalStorage storage = new();

    public TestRunnerTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton<ILocalStorage>(this.storage);
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private void UseResult(ScriptTestResultDto result)
        => this.Services.AddSingleton(new ScriptsClient(FakeHttpMessageHandler.Json(JsonSerializer.Serialize(result, ApiHttp.Json)).CreateClient()));

    private IRenderedComponent<TestRunnerPanel> Render(string code = "return 1;", string language = "javascript", bool canRun = true)
        => this.RenderComponent<TestRunnerPanel>(p => p
            .Add(x => x.Code, code)
            .Add(x => x.Language, language)
            .Add(x => x.CanRun, canRun));

    [Fact]
    public void Test_Success_ShowsResult_Logs_Duration()
    {
        this.UseResult(new ScriptTestResultDto(
            true, El("{\"total\":10}"),
            new List<ScriptLogEntryDto> { new("Info", "processed order-1") },
            new Dictionary<string, JsonElement>(), 12.0, null));

        var cut = this.Render();
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=run-success]").TextContent.Should().Contain("12ms");
            cut.Find("[data-testid=run-result]").TextContent.Should().Contain("total");
            cut.FindAll("[data-testid=log-line]").Should().ContainSingle(l => l.TextContent.Contains("processed order-1"));
        });
    }

    [Fact]
    public void Test_Failure_ShowsStructuredError_InPanel()
    {
        this.UseResult(new ScriptTestResultDto(
            false, El("null"), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 3.0, "ReferenceError: x is not defined"));

        var cut = this.Render();
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=run-failure]").TextContent.Should().Contain("ReferenceError"));
    }

    [Fact]
    public void VariableUpdates_Rendered()
    {
        this.UseResult(new ScriptTestResultDto(
            true, El("{}"), new List<ScriptLogEntryDto>(),
            new Dictionary<string, JsonElement> { ["t"] = El("10") }, 1.0, null));

        var cut = this.Render();
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=var-update]").TextContent.Should().Contain("t = 10"));
    }

    [Fact]
    public void Test_SendsCodeLanguageInputsConfig()
    {
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(
            new ScriptTestResultDto(true, El("{}"), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 1.0, null), ApiHttp.Json));
        this.Services.AddSingleton(new ScriptsClient(handler.CreateClient()));

        var cut = this.Render(code: "return input.x;");
        cut.Find("[data-testid=inputs-json]").Input("{\"x\":42}");
        cut.Find("[data-testid=allow-network]").Change(true);
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() =>
        {
            handler.Bodies.Should().ContainSingle();
            var body = handler.Bodies[0];
            body.Should().Contain("\"language\":\"javascript\"");
            body.Should().Contain("return input.x;");
            body.Should().Contain("\"x\":42");
            body.Should().Contain("\"allowNetwork\":true");
        });
    }

    [Fact]
    public void Test_InvalidInputs_ShownInPanel_NoRequest()
    {
        var handler = FakeHttpMessageHandler.Json("{}");
        this.Services.AddSingleton(new ScriptsClient(handler.CreateClient()));

        var cut = this.Render();
        cut.Find("[data-testid=inputs-json]").Input("{ not json");
        cut.Find("[data-testid=run-test]").Click();

        cut.Find("[data-testid=inputs-error]").Should().NotBeNull();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Test_Unknown_422_ShownInPanel()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"title\":\"Validation failed\",\"detail\":\"Unknown script language 'ruby'.\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        this.Services.AddSingleton(new ScriptsClient(handler.CreateClient()));

        var cut = this.Render(language: "ruby");
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=request-error]").TextContent.Should().Contain("ruby"));
    }

    [Fact]
    public void Run_PythonOrEmpty_DisabledWithHint()
    {
        this.UseResult(new ScriptTestResultDto(true, El("{}"), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 1.0, null));

        var cut = this.Render(code: "print('x')", language: "python", canRun: false);

        cut.Find("[data-testid=run-test]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid=run-hint]").Should().NotBeNull();
    }

    [Fact]
    public void Inputs_RememberedPerLanguage()
    {
        this.UseResult(new ScriptTestResultDto(true, El("{}"), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 1.0, null));

        var cut = this.Render();
        cut.Find("[data-testid=inputs-json]").Input("{\"remembered\":true}");
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() => this.storage.Store.Should().ContainKey("dotflow.scriptinputs.javascript"));
        this.storage.Store["dotflow.scriptinputs.javascript"].Should().Contain("remembered");
    }

    private sealed class FakeLocalStorage : ILocalStorage
    {
        public Dictionary<string, string> Store { get; } = new();

        public ValueTask<string?> GetAsync(string key) => new(this.Store.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetAsync(string key, string value)
        {
            this.Store[key] = value;
            return default;
        }

        public ValueTask RemoveAsync(string key)
        {
            this.Store.Remove(key);
            return default;
        }
    }
}
