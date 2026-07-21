// <copyright file="ScriptsClientTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Scripts.State;
using Xunit;

/// <summary>
/// 🧪 Phase 3.4.0 — Tests for the typed <see cref="ScriptsClient"/> + the framework-free
/// <see cref="ScriptEditorOptions"/> language map~ ✨.
/// </summary>
public sealed class ScriptsClientTests
{
    [Fact]
    public async Task ScriptsClient_Test_SendsRequestShape()
    {
        var result = new ScriptTestResultDto(true, JsonDocument.Parse("{\"total\":10}").RootElement.Clone(), new List<ScriptLogEntryDto>(), new Dictionary<string, JsonElement>(), 12.0, null);
        var handler = FakeHttpMessageHandler.Json(JsonSerializer.Serialize(result, ApiHttp.Json));
        var client = new ScriptsClient(handler.CreateClient());

        var request = new ScriptTestRequestDto(
            "javascript",
            "return { total: 10 };",
            new Dictionary<string, JsonElement> { ["x"] = JsonDocument.Parse("1").RootElement.Clone() },
            new List<string> { "order-utils" },
            new ScriptTestConfigDto(TimeoutSeconds: 15, AllowNetwork: true));

        var res = await client.TestAsync(request);

        res.Success.Should().BeTrue();
        res.DurationMs.Should().Be(12.0);
        handler.Requests.Should().ContainSingle(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/scripts/test"));
        var body = handler.Bodies[0];
        body.Should().Contain("\"language\":\"javascript\"");
        body.Should().Contain("\"code\":");
        body.Should().Contain("order-utils");
        body.Should().Contain("\"timeoutSeconds\":15");
        body.Should().Contain("\"allowNetwork\":true");
    }

    [Fact]
    public async Task ScriptsClient_Languages_Parses()
    {
        var handler = FakeHttpMessageHandler.Json("[{\"languageId\":\"javascript\",\"displayName\":\"JavaScript\"},{\"languageId\":\"lua\",\"displayName\":\"Lua\"}]");
        var client = new ScriptsClient(handler.CreateClient());

        var langs = await client.GetLanguagesAsync();

        langs.Should().HaveCount(2);
        langs[0].LanguageId.Should().Be("javascript");
        langs[0].DisplayName.Should().Be("JavaScript");
    }

    [Fact]
    public async Task ScriptsClient_Libraries_CrudRoundTrips()
    {
        var lib = new ScriptLibraryDto("order-utils", "Order Utils", "helpers", "javascript", "function f(){}", new List<string> { "f" }, new List<string>());
        var handler = new FakeHttpMessageHandler(req => req.Method == HttpMethod.Delete
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("libraries") ? (object)new List<ScriptLibraryDto> { lib } : lib, ApiHttp.Json)) });
        var client = new ScriptsClient(handler.CreateClient());

        var list = await client.ListLibrariesAsync("javascript");
        list.Should().ContainSingle(l => l.LibraryId == "order-utils");

        var saved = await client.SaveLibraryAsync(lib);
        saved.Name.Should().Be("Order Utils");

        await client.DeleteLibraryAsync("order-utils");

        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Put && r.RequestUri!.AbsolutePath.EndsWith("/scripts/libraries/order-utils"));
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Delete);
        handler.Requests.Should().Contain(r => r.Method == HttpMethod.Get && r.RequestUri!.Query.Contains("language=javascript"));
    }

    [Fact]
    public async Task ScriptsClient_ServerError_SurfacesProblemDetails()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"title\":\"Validation failed\",\"detail\":\"Unknown script language 'ruby'.\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var client = new ScriptsClient(handler.CreateClient());

        var act = () => client.TestAsync(new ScriptTestRequestDto("ruby", "x"));

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.Error.StatusCode.Should().Be(422);
        ex.Which.Error.Detail.Should().Contain("ruby");
    }

    [Theory]
    [InlineData("javascript", "javascript", true)]
    [InlineData("csharp", "csharp", true)]
    [InlineData("lua", "lua", true)]
    [InlineData("python", "python", false)]
    [InlineData("json", "json", false)]
    public void EditorOptions_LanguageMap_CoversRegisteredLanguages(string language, string expectedMode, bool runnable)
    {
        ScriptEditorOptions.MonacoMode(language).Should().Be(expectedMode);
        ScriptEditorOptions.IsHighlightable(language).Should().BeTrue();
        ScriptEditorOptions.IsKnownRunnable(language).Should().Be(runnable);
    }

    [Fact]
    public void EditorOptions_UnknownLanguage_FallsBackToPlaintext()
    {
        ScriptEditorOptions.MonacoMode("brainfuck").Should().Be("plaintext");
        ScriptEditorOptions.IsHighlightable("brainfuck").Should().BeFalse();
    }
}
