// <copyright file="WorkflowScriptApiTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Api;
using Xunit;

/// <summary>
/// 🔧 Phase 3.1.1 — Tests for the capability-gated workflow script API~ ✨.
/// </summary>
public sealed class WorkflowScriptApiTests
{
    private static WorkflowScriptApi Api(ScriptExecutionConfig? config = null, IReadOnlyDictionary<string, object?>? variables = null, IHttpClientFactory? httpFactory = null)
        => new(new WorkflowScriptApiOptions
        {
            Variables = variables ?? new Dictionary<string, object?>(),
            Config = config ?? ScriptExecutionConfig.Default,
            NodeId = "n1",
            Logger = NullLogger.Instance,
            HttpClientFactory = httpFactory,
        });

    // ── Variables ──
    [Fact]
    public void SetVariable_StagedInResult()
    {
        var api = Api();
        api.SetVariable("x", 42);
        api.GetVariableUpdates().Should().ContainKey("x").WhoseValue.Should().Be(42);
    }

    [Fact]
    public void GetVariable_ReadsSnapshot_AndStagedOverride()
    {
        var api = Api(variables: new Dictionary<string, object?> { ["a"] = 1 });
        api.GetVariable("a").Should().Be(1);
        api.SetVariable("a", 2);
        api.GetVariable("a").Should().Be(2, "staged writes shadow the snapshot");
    }

    [Fact]
    public void VariableExists_Works()
    {
        var api = Api(variables: new Dictionary<string, object?> { ["a"] = 1 });
        api.VariableExists("a").Should().BeTrue();
        api.VariableExists("nope").Should().BeFalse();
    }

    [Fact]
    public void DeleteVariable_StagesNull()
    {
        var api = Api(variables: new Dictionary<string, object?> { ["a"] = 1 });
        api.DeleteVariable("a");
        api.GetVariableUpdates().Should().ContainKey("a").WhoseValue.Should().BeNull();
    }

    // ── Logging ──
    [Fact]
    public void Logs_CapturedAtAllLevels()
    {
        var api = Api();
        api.LogDebug("d");
        api.LogInfo("i");
        api.LogWarning("w");
        api.LogError("e");
        api.GetLogs().Select(l => l.Level).Should().ContainInOrder("debug", "info", "warning", "error");
    }

    // ── Utilities ──
    [Fact]
    public void Utilities_RoundTrip()
    {
        var api = Api();
        api.Base64Decode(api.Base64Encode("hello")).Should().Be("hello");
        api.Hash("abc", "sha256").Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
        Guid.TryParse(api.NewGuid(), out _).Should().BeTrue();
        api.ToJson(new Dictionary<string, object?> { ["a"] = 1 }).Should().Contain("\"a\"");
        (api.ParseJson("{\"a\":1}") as IReadOnlyDictionary<string, object?>)!["a"].Should().Be(1L);
    }

    [Fact]
    public void Csv_RoundTrips()
    {
        var api = Api();
        var parsed = api.ParseCsv("name,age\nAmi,3\nBo,5", hasHeader: true);
        var rows = parsed.Should().BeAssignableTo<IEnumerable<object?>>().Subject.ToList();
        rows.Should().HaveCount(2);

        var csv = api.ToCsv(rows, includeHeader: true);
        csv.Should().Contain("name,age").And.Contain("Ami,3");
    }

    // ── HTTP gating ──
    [Fact]
    public async Task Http_Blocked_WhenNetworkDisallowed()
    {
        var api = Api();
        var act = async () => await api.HttpGetAsync("http://localhost/");
        await act.Should().ThrowAsync<ScriptSecurityException>();
    }

    [Fact]
    public async Task Http_Allowed_MakesRequest()
    {
        using var server = new StubHttpServer("hello-from-server");
        var api = Api(
            config: ScriptExecutionConfig.Default with { AllowNetwork = true },
            httpFactory: new SingleClientFactory());

        var response = await api.HttpGetAsync(server.Url);

        var dict = response.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        dict["status"].Should().Be(200);
        dict["body"].Should().Be("hello-from-server");
    }

    [Fact]
    public async Task Http_RequestCount_CapEnforced()
    {
        using var server = new StubHttpServer("ok");
        var api = Api(
            config: ScriptExecutionConfig.Default with { AllowNetwork = true, MaxHttpRequests = 1 },
            httpFactory: new SingleClientFactory());

        await api.HttpGetAsync(server.Url);
        var act = async () => await api.HttpGetAsync(server.Url);
        await act.Should().ThrowAsync<ScriptSecurityException>().WithMessage("*maximum*");
    }

    // ── File gating ──
    [Fact]
    public async Task File_Blocked_WhenFileSystemDisallowed()
    {
        var api = Api();
        var act = async () => await api.ReadFileAsync("C:/whatever.txt");
        await act.Should().ThrowAsync<ScriptSecurityException>();
    }

    [Fact]
    public async Task File_AllowedPath_ReadsWrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "script-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var api = Api(config: ScriptExecutionConfig.Default with { AllowFileSystem = true, AllowedPaths = new[] { dir } });
            var file = Path.Combine(dir, "note.txt");

            await api.WriteFileAsync(file, "content");
            api.FileExists(file).Should().BeTrue();
            (await api.ReadFileAsync(file)).Should().Be("content");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task File_OutsideAllowedPaths_Rejected()
    {
        var allowed = Path.Combine(Path.GetTempPath(), "allowed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowed);
        try
        {
            var api = Api(config: ScriptExecutionConfig.Default with { AllowFileSystem = true, AllowedPaths = new[] { allowed } });
            var outside = Path.Combine(Path.GetTempPath(), "elsewhere.txt");

            var act = async () => await api.ReadFileAsync(outside);
            await act.Should().ThrowAsync<ScriptSecurityException>().WithMessage("*outside the allowed paths*");
        }
        finally
        {
            Directory.Delete(allowed, recursive: true);
        }
    }

    [Fact]
    public async Task File_PathTraversal_Rejected()
    {
        var allowed = Path.Combine(Path.GetTempPath(), "allowed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowed);
        try
        {
            var api = Api(config: ScriptExecutionConfig.Default with { AllowFileSystem = true, AllowedPaths = new[] { allowed } });
            var traversal = Path.Combine(allowed, "..", "secret.txt");

            var act = async () => await api.ReadFileAsync(traversal);
            await act.Should().ThrowAsync<ScriptSecurityException>();
        }
        finally
        {
            Directory.Delete(allowed, recursive: true);
        }
    }

    [Fact]
    public async Task Wait_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        var api = new WorkflowScriptApi(new WorkflowScriptApiOptions
        {
            Config = ScriptExecutionConfig.Default,
            Logger = NullLogger.Instance,
            CancellationToken = cts.Token,
        });
        cts.Cancel();

        var act = async () => await api.WaitAsync(5000);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubHttpServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cts = new();

        public StubHttpServer(string body)
        {
            var port = 5000 + Random.Shared.Next(1000, 9000);
            this.Url = $"http://localhost:{port}/";
            this.listener.Prefixes.Add(this.Url);
            this.listener.Start();
            _ = Task.Run(async () =>
            {
                while (!this.cts.IsCancellationRequested)
                {
                    HttpListenerContext ctx;
                    try
                    {
                        ctx = await this.listener.GetContextAsync();
                    }
                    catch
                    {
                        return;
                    }

                    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
            });
        }

        public string Url { get; }

        public void Dispose()
        {
            this.cts.Cancel();
            this.listener.Close();
        }
    }
}
