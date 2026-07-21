// <copyright file="ScriptingCoreTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Compilation;
using Workflow.Scripting.Roslyn.Execution;
using Xunit;

/// <summary>
/// 🧬 Phase 2.6.b.0 — tests for the shared, domain-agnostic Roslyn scripting core~ ✨.
/// </summary>
public sealed class ScriptingCoreTests
{
    private const string EntryType = "WorkflowRuntime.TransformScript";

    private static string WrapBody(string body) =>
        "using System;\nusing System.Linq;\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\n" +
        "namespace WorkflowRuntime {\n public static class TransformScript {\n" +
        "  public static async Task<object?> ExecuteAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> inputs, CancellationToken ct) {\n" +
        "   await Task.CompletedTask;\n" + body + "\n } } }";

    [Theory]
    [InlineData("System.IO.File.ReadAllText(\"x\");")]
    [InlineData("var p = System.Diagnostics.Process.Start(\"cmd\");")]
    [InlineData("var c = new System.Net.Http.HttpClient();")]
    public void ForbiddenSyntaxWalker_SharedBlocklist_RejectsDangerousReaches(string body)
    {
        var violations = ForbiddenSyntaxWalker.Scan(body);
        violations.Should().NotBeEmpty();
    }

    [Fact]
    public void ForbiddenSyntaxWalker_CleanBody_NoViolations()
    {
        ForbiddenSyntaxWalker.Scan("return rows.Count;").Should().BeEmpty();
    }

    [Fact]
    public void Compiler_ValidBody_Succeeds()
    {
        var compiler = new RoslynScriptCompiler();
        var result = compiler.Compile(new ScriptCompileRequest("A", WrapBody("return rows.Count;"), "return rows.Count;"));
        result.Success.Should().BeTrue();
        result.AssemblyBytes.Should().NotBeNull();
    }

    [Fact]
    public void Compiler_ForbiddenReach_RejectedByWalker()
    {
        var compiler = new RoslynScriptCompiler();
        var body = "System.IO.File.Delete(\"x\"); return null;";
        var result = compiler.Compile(new ScriptCompileRequest("A", WrapBody(body), body));
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id == "WFSCRIPT100");
    }

    [Fact]
    public void Compiler_SyntaxError_ReturnsDiagnostics()
    {
        var compiler = new RoslynScriptCompiler();
        var body = "return this is not valid;";
        var result = compiler.Compile(new ScriptCompileRequest("A", WrapBody(body), body));
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Hmac_SignVerify_RoundTrips_And_DetectsTamper()
    {
        var signer = new HmacScriptAssemblySigner(new EphemeralScriptHmacKeyProvider());
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var signed = signer.Sign(payload);

        signer.TryVerify(signed, out var verified).Should().BeTrue();
        verified.Should().BeEquivalentTo(payload);

        signed[^1] ^= 0xFF;
        signer.TryVerify(signed, out _).Should().BeFalse();
    }

    [Fact]
    public void AssemblyKey_ChangesWithCode()
    {
        var a = ScriptAssemblyKey.Build("compiled-modules/transform", "def", "node", "code A", 1, "in");
        var b = ScriptAssemblyKey.Build("compiled-modules/transform", "def", "node", "code B", 1, "in");
        a.Should().NotBe(b);
        a.Should().StartWith("compiled-modules/transform/def/node/");
    }

    [Fact]
    public async Task CollectibleRunner_LoadRunUnload_Works()
    {
        var compiler = new RoslynScriptCompiler();
        var body = "return rows.Count + (int)(long)inputs[\"bonus\"];";
        var compiled = compiler.Compile(new ScriptCompileRequest("R", WrapBody(body), body));
        compiled.Success.Should().BeTrue();

        using var runner = new CollectibleScriptRunner();
        var rows = new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?>(), new Dictionary<string, object?>() };
        var inputs = new Dictionary<string, object?> { ["bonus"] = 10L };

        var result = await runner.RunAsync("k1", compiled.AssemblyBytes!, EntryType, "ExecuteAsync", new object?[] { rows, inputs, CancellationToken.None });
        result.Should().Be(12);
        runner.LoadedAssemblyCount.Should().Be(1);

        // Reuse — same key doesn't grow ALC count~
        await runner.RunAsync("k1", compiled.AssemblyBytes!, EntryType, "ExecuteAsync", new object?[] { rows, inputs, CancellationToken.None });
        runner.LoadedAssemblyCount.Should().Be(1);
    }

    [Fact]
    public async Task CompiledScriptCache_StoreVerifyGet_And_TamperMiss()
    {
        var blob = new InMemoryBlobStore();
        var signer = new HmacScriptAssemblySigner(new EphemeralScriptHmacKeyProvider());
        var cache = new CompiledScriptCache(blob, signer);

        var bytes = new byte[] { 9, 8, 7, 6 };
        var key = "compiled-modules/transform/d/n/hash.dll";
        await cache.StoreAsync(key, bytes);

        (await cache.TryGetAsync(key)).Should().BeEquivalentTo(bytes);

        blob.Corrupt(key);
        // LRU still has the good copy; use a fresh cache to force a blob read~
        var freshCache = new CompiledScriptCache(blob, signer);
        (await freshCache.TryGetAsync(key)).Should().BeNull("tampered blob must verify-fail~ 🛡️");
    }
}
