// <copyright file="DesignerIntegrationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Collections.Generic;
using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.Components;
using Workflow.UI.Client.Designer.State;
using Workflow.UI.Client.Designer.State.Commands;
using Workflow.UI.Client.Scripts.State;
using Workflow.UI.Client.Services;
using ClientScriptStudio = Workflow.UI.Client.Pages.ScriptStudio;
using Xunit;

/// <summary>
/// 🔗 Phase 3.4.5 — Designer ↔ Script Studio round-trip + keyboard-shortcut tests~ ✨.
/// </summary>
public sealed class DesignerIntegrationTests : TestContext
{
    private readonly ScriptStudioHandoff handoff = new();

    public DesignerIntegrationTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
        this.Services.AddSingleton<ILocalStorage>(new InMemoryLocalStorage());
        this.Services.AddSingleton(this.handoff);
    }

    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private void UseLanguages()
        => this.Services.AddSingleton(new ScriptsClient(
            FakeHttpMessageHandler.Json("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]").CreateClient()));

    [Fact]
    public void ScriptNode_EditButton_OpensStudio_Seeded()
    {
        var props = new List<ModulePropertyDefinitionDto>
        {
            new("code", "Code", "String", null, false, null, "Code", null),
            new("language", "Language", "String", null, false, null, "Select", new List<JsonElement> { El("\"javascript\"") }),
        };
        var node = new DesignerNode
        {
            Id = "script-1",
            ModuleId = "builtin.script",
            Name = "My Script",
            Schema = new ModuleSchemaDto(new(), new(), props),
        };
        node.Properties["code"] = El("\"return 1;\"");
        node.Properties["language"] = El("\"javascript\"");

        var doc = new DesignerDocument();
        doc.Nodes.Add(node);
        var sel = new SelectionState();
        sel.SelectNode("script-1");

        var cut = this.RenderComponent<PropertiesPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.Selection, sel)
            .Add(x => x.Commands, new CommandStack(doc)));

        cut.Find("[data-testid=edit-in-studio]").Click();

        this.handoff.HasRequest.Should().BeTrue();
        this.handoff.NodeId.Should().Be("script-1");
        this.handoff.Code.Should().Contain("return 1;");
        this.handoff.Language.Should().Be("javascript");
    }

    [Fact]
    public void Studio_ApplyBack_ReturnsCodeToNode()
    {
        this.UseLanguages();
        this.handoff.Request("script-1", "return 1;", "javascript", "/designer/abc");

        var cut = this.RenderComponent<ClientScriptStudio>();
        // The studio seeds from the request and offers "Apply to node".
        cut.WaitForAssertion(() => cut.Find("[data-testid=script-textarea]").GetAttribute("value").Should().Contain("return 1;"));

        cut.Find("[data-testid=script-textarea]").Input("return 42;");
        cut.Find("[data-testid=apply-to-node]").Click();

        // The result is staged for the designer.
        this.handoff.HasResult.Should().BeTrue();
        var result = this.handoff.TakeResult();
        result.Should().NotBeNull();
        result!.Value.NodeId.Should().Be("script-1");
        result.Value.Code.Should().Contain("return 42;");

        // …and the designer applies it as an EditNodePropertiesCommand.
        var node = new DesignerNode { Id = "script-1", ModuleId = "builtin.script", Name = "S" };
        node.Properties["code"] = El("\"return 1;\"");
        var before = new Dictionary<string, JsonElement>(node.Properties);
        var after = new Dictionary<string, JsonElement>(node.Properties) { ["code"] = JsonValues.FromString(result.Value.Code) };
        var doc = new DesignerDocument();
        doc.Nodes.Add(node);
        var stack = new CommandStack(doc);
        stack.Execute(new EditNodePropertiesCommand("script-1", before, after));

        JsonValues.ToText(doc.FindNode("script-1")!.Properties["code"]).Should().Contain("return 42;");
    }

    [Fact]
    public void Shortcut_CtrlS_Saves()
    {
        this.UseLanguages();

        var cut = this.RenderComponent<ClientScriptStudio>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=language-select]"));
        cut.FindAll("[data-testid=save-dialog]").Should().BeEmpty();

        cut.InvokeAsync(() => cut.Instance.OnShortcut("s", true, false));

        cut.WaitForAssertion(() => cut.Find("[data-testid=save-dialog]").Should().NotBeNull());
    }

    [Fact]
    public void Shortcut_CtrlEnter_RunsTest()
    {
        var testResult = JsonSerializer.Serialize(
            new ScriptTestResultDto(true, El("{}"), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 1.0, null), ApiHttp.Json);
        var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/scripts/languages")
                ? new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"}]") }
                : new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(testResult) });
        this.Services.AddSingleton(new ScriptsClient(handler.CreateClient()));

        var cut = this.RenderComponent<ClientScriptStudio>();
        cut.WaitForAssertion(() => cut.Find("[data-testid=script-textarea]"));
        cut.Find("[data-testid=script-textarea]").Input("return 1;");

        cut.InvokeAsync(() => cut.Instance.OnShortcut("Enter", true, false));

        cut.WaitForAssertion(() => handler.Requests.Should().Contain(r => r.RequestUri!.AbsolutePath.EndsWith("/scripts/test")));
    }
}
