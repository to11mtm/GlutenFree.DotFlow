// <copyright file="ScriptApprovalUiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System;
using System.Collections.Generic;
using System.Linq;
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
using Workflow.UI.Client.Pages;
using Workflow.UI.Client.Scripts.Components;
using Workflow.UI.Client.Services;
using Workflow.UI.Client.Shared;
using Xunit;

/// <summary>
/// §9.2 UI — blocked C# code shows each sensitive API with its danger explained and lets the author
/// request review; reviewers see the exact code and approve or reject it.
/// </summary>
public sealed class ScriptApprovalUiTests : TestContext
{
    private const string Hash = "abc123";

    private static readonly List<SensitiveApiFlagDto> Flags = new()
    {
        new("System.IO.File.Exists(string)", "FileSystem", "File system access", "Can read, write or delete any file the server process can reach.", 2, 8),
        new("System.Type.GetType(string)", "Reflection", "Reflection / late binding", "Reflection can look up any type and call any method by name.", 3, 9),
        new("System.Type.GetMethod(string)", "Reflection", "Reflection / late binding", "Reflection can look up any type and call any method by name.", 3, 30),
    };

    public ScriptApprovalUiTests()
    {
        this.JSInterop.Mode = JSRuntimeMode.Loose;
        this.Services.AddSingleton<ILocalStorage>(new FakeLocalStorage());
        this.Services.AddSingleton(new ToastService());
        this.Services.AddSingleton(new AuthState());
        this.Services.AddSingleton(new ApiClientOptions { BaseUrl = "http://localhost" });
    }

    [Fact]
    public void ReviewPanel_ExplainsEachCategoryOnce_AndListsEverySymbol()
    {
        this.UseApprovals(_ => NotFound());

        var cut = this.RenderComponent<SecurityReviewPanel>(p => p.Add(x => x.Flags, Flags).Add(x => x.CodeHash, Hash));

        cut.FindAll("[data-testid=security-flag]").Should().HaveCount(2, because: "flags are grouped by category");
        cut.FindAll("[data-testid=security-danger]").Select(d => d.TextContent)
            .Should().Contain(t => t.Contains("read, write or delete")).And.Contain(t => t.Contains("call any method"));
        cut.Markup.Should().Contain("System.Type.GetMethod(string)").And.Contain("line 3");
        cut.Find("[data-testid=security-status]").TextContent.Should().Contain("Not requested yet");
    }

    [Fact]
    public void ReviewPanel_RequestApproval_SendsTheNote_AndShowsWaiting()
    {
        var handler = this.UseApprovals(req => req.Method == HttpMethod.Post
            ? Ok(Record("Requested", requestedBy: "alice"))
            : NotFound());

        var cut = this.RenderComponent<SecurityReviewPanel>(p => p.Add(x => x.Flags, Flags).Add(x => x.CodeHash, Hash));
        cut.Find("[data-testid=security-note]").Change("needs a marker file");
        cut.Find("[data-testid=security-request]").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid=security-status]").TextContent.Should().Contain("Waiting for a reviewer"));
        handler.Requests.Last().RequestUri!.AbsolutePath.Should().Be($"/api/v1/script-approvals/{Hash}/request");
        handler.Bodies.Last().Should().Contain("needs a marker file");
        cut.FindAll("[data-testid=security-request]").Should().BeEmpty("you can't request twice");
    }

    [Fact]
    public void ReviewPanel_ShowsTheReviewersRejectionNote()
    {
        this.UseApprovals(_ => Ok(Record("Rejected", requestedBy: "alice", decidedBy: "bob", decisionNote: "use the HTTP module")));

        var cut = this.RenderComponent<SecurityReviewPanel>(p => p.Add(x => x.Flags, Flags).Add(x => x.CodeHash, Hash));

        cut.WaitForAssertion(() => cut.Find("[data-testid=security-status]").TextContent
            .Should().Contain("Rejected by bob").And.Contain("use the HTTP module"));
        cut.FindAll("[data-testid=security-request]").Should().ContainSingle("a rejected script can be requested again after changes or justification");
    }

    [Fact]
    public void TestRunner_BlockedCSharp_ShowsTheReviewPanel()
    {
        var blocked = new ScriptTestResultDto(
            false, JsonDocument.Parse("null").RootElement.Clone(), new List<ScriptLogEntryDto>(),
            new Dictionary<string, JsonElement>(), 5.0, "C# compile failed: needs reviewer approval", Flags, Hash);
        this.Services.AddSingleton(new ScriptsClient(FakeHttpMessageHandler.Json(JsonSerializer.Serialize(blocked, ApiHttp.Json)).CreateClient()));
        this.UseApprovals(_ => NotFound());

        var cut = this.RenderComponent<TestRunnerPanel>(p => p
            .Add(x => x.Code, "return System.IO.File.Exists(\"x\");")
            .Add(x => x.Language, "csharp")
            .Add(x => x.CanRun, true));
        cut.Find("[data-testid=run-test]").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid=run-failure]");
            cut.Find("[data-testid=security-review]").TextContent.Should().Contain("File system access");
        });
    }

    [Fact]
    public void ApprovalsPage_ShowsCodeAndDangers_AndApproves()
    {
        var handler = this.UseApprovals(req => req.Method == HttpMethod.Post
            ? Ok(Record("Approved", requestedBy: "alice", decidedBy: "bob"))
            : OkList(Record("Requested", requestedBy: "alice", requestNote: "checks a marker file")));

        var cut = this.RenderComponent<ScriptApprovals>();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=approval-item]").Should().ContainSingle());
        cut.Find("[data-testid=approval-code]").TextContent.Should().Contain("System.IO.File.Exists");
        cut.Find("[data-testid=request-note]").TextContent.Should().Contain("checks a marker file");
        cut.FindAll("[data-testid=approval-danger]").Should().HaveCount(2);

        cut.Find("[data-testid=decision-note]").Change("read-only check");
        cut.Find("[data-testid=approve]").Click();

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=approval-item]").Should().BeEmpty("approved items leave the Requested tab"));
        handler.Requests.Last().RequestUri!.AbsolutePath.Should().Be($"/api/v1/script-approvals/{Hash}/approve");
        handler.Bodies.Last().Should().Contain("read-only check");
    }

    [Fact]
    public void ApprovalsPage_SelfApprovalError_IsShownAsAToast()
    {
        this.UseApprovals(req => req.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"title\":\"Forbidden\",\"status\":403,\"detail\":\"You requested this approval, so someone else has to review it.\"}", System.Text.Encoding.UTF8, "application/problem+json"),
            }
            : OkList(Record("Requested", requestedBy: "alice")));
        var toasts = this.Services.GetRequiredService<ToastService>();

        var cut = this.RenderComponent<ScriptApprovals>();
        cut.WaitForAssertion(() => cut.FindAll("[data-testid=approve]").Should().ContainSingle());
        cut.Find("[data-testid=approve]").Click();

        cut.WaitForAssertion(() => toasts.Toasts.Should().Contain(t => t.Message.Contains("someone else")));
        cut.FindAll("[data-testid=approval-item]").Should().ContainSingle("a failed approval leaves the item in the queue");
    }

    private FakeHttpMessageHandler UseApprovals(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        this.Services.AddSingleton(new ScriptApprovalsClient(handler.CreateClient()));
        return handler;
    }

    private static ScriptApprovalDto Record(
        string status,
        string? requestedBy = null,
        string? decidedBy = null,
        string? requestNote = null,
        string? decisionNote = null)
        => new(
            Hash, "csharp-script", status, "var x = 1;\nreturn System.IO.File.Exists(\"x\");", Flags,
            DateTimeOffset.UtcNow, requestedBy, requestedBy is null ? null : DateTimeOffset.UtcNow, requestNote,
            decidedBy, decidedBy is null ? null : DateTimeOffset.UtcNow, decisionNote);

    private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, ApiHttp.Json), System.Text.Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage OkList(params ScriptApprovalDto[] records) => Ok(records.ToList());

    private static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound)
    {
        Content = new StringContent("{\"title\":\"Not Found\",\"status\":404}", System.Text.Encoding.UTF8, "application/problem+json"),
    };

    private sealed class FakeLocalStorage : ILocalStorage
    {
        private readonly Dictionary<string, string> store = new();

        public ValueTask<string?> GetAsync(string key) => new(this.store.TryGetValue(key, out var v) ? v : null);

        public ValueTask SetAsync(string key, string value)
        {
            this.store[key] = value;
            return default;
        }

        public ValueTask RemoveAsync(string key)
        {
            this.store.Remove(key);
            return default;
        }
    }
}
