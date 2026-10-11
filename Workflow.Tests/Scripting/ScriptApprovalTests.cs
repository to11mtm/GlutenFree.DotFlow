// <copyright file="ScriptApprovalTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Scripting.Approvals;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Compilation;
using Workflow.Scripting.Roslyn.Security;
using Xunit;

/// <summary>
/// §9.2 — C# scripts and LINQ bodies are checked semantically for sensitive APIs (reflection, IO,
/// network, process, interop, …) and only compile when a reviewer approved that exact code.
/// </summary>
public sealed class ScriptApprovalTests
{
    // ── Semantic check: reflection can't sneak past by spelling ──────────────────────────────────

    [Theory]
    [InlineData("var t = System.Type.GetType(\"System.Text.StringBuilder\"); return t != null;")]
    [InlineData("var t = global::System.Type.GetType(\"System.IO.File\"); return t;")]
    [InlineData("return typeof(string).GetMethods().Length;")]
    [InlineData("return typeof(string).Assembly.FullName;")]
    [InlineData("object o = \"x\"; return o.GetType().GetProperties().Length;")]
    [InlineData("dynamic d = rows; return d.Count;")]
    public void Script_ReflectionInAnySpelling_IsFlaggedAndDenied(string body)
    {
        var result = Compile(body);

        result.Success.Should().BeFalse();
        result.NeedsApproval.Should().BeTrue();
        result.SecurityFlags.Should().Contain(f => f.Category == SensitiveApiCatalog.Reflection);
    }

    [Theory]
    [InlineData("return System.IO.File.ReadAllText(\"/etc/passwd\");", SensitiveApiCatalog.FileSystem)]
    [InlineData("var c = new System.Net.Http.HttpClient(); return c;", SensitiveApiCatalog.Network)]
    [InlineData("System.Diagnostics.Process.Start(\"cmd\"); return null;", SensitiveApiCatalog.Process)]
    [InlineData("return System.Environment.GetEnvironmentVariable(\"SECRET\");", SensitiveApiCatalog.Environment)]
    [InlineData("return System.Runtime.InteropServices.Marshal.SizeOf<int>();", SensitiveApiCatalog.InteropAndMemory)]
    [InlineData("return System.Reflection.Assembly.LoadFrom(\"x.dll\");", SensitiveApiCatalog.AssemblyLoading)]
    public void Script_DirectSensitiveApi_IsFlaggedInItsCategory(string body, string category)
    {
        var result = Compile(body);

        result.Success.Should().BeFalse();
        result.SecurityFlags.Should().Contain(f => f.Category == category);
    }

    [Theory]
    [InlineData("var m = System.Type.GetType(\"System.IO.File\")!.GetMethod(\"ReadAllText\", new[] { typeof(string) }); return m!.Invoke(null, new object[] { \"x\" });")]
    [InlineData("var t = System.Type.GetType(\"System.Net.Http.HttpClient, System.Net.Http\"); return System.Activator.CreateInstance(t!);")]
    [InlineData("var t = System.Type.GetType(\"System.Diagnostics.Process, System.Diagnostics.Process\"); return t!.GetMethod(\"Start\", new[] { typeof(string) })!.Invoke(null, new object[] { \"cmd\" });")]
    public void Script_IoNetOrProcessReachedByReflection_IsDenied(string body)
    {
        var result = Compile(body);

        result.Success.Should().BeFalse("IO / network / process reached by reflection must still need approval (§9.2)");
        result.Diagnostics.Should().Contain(d => d.Id == "WFSCRIPT100");
    }

    [Fact]
    public void Script_CleanCode_HasNoFlags_AndCompiles()
    {
        var result = Compile("var sb = new System.Text.StringBuilder(); sb.Append(rows.Count); using var ms = new System.IO.MemoryStream(); return sb.ToString();");

        result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
        result.SecurityFlags.Should().BeEmpty();
        result.CodeHash.Should().BeNull();
    }

    [Fact]
    public void Flags_CarryTheDangerExplanation_AndPositionInTheUserBody()
    {
        var result = Compile("var x = 1;\nreturn System.IO.File.Exists(\"a\");");

        var flag = result.SecurityFlags!.First(f => f.Category == SensitiveApiCatalog.FileSystem);
        flag.Title.Should().Be("File system access");
        flag.Danger.Should().Contain("read, write or delete");
        flag.Line.Should().Be(2, because: "positions are relative to the user's code, not the generated wrapper");
        result.Diagnostics.First().Message.Should().Contain(result.CodeHash!);
    }

    [Fact]
    public void LocalNamedLikeABlockedType_IsNotFlagged()
    {
        // The old name blocklist rejected any identifier called Path; binding knows it's a local~
        var result = Compile("var Path = \"a/b\"; return Path.Length;");

        result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void UnsafeCode_StaysAHardError_NotReviewable()
    {
        var result = Compile("unsafe { int x = 0; int* p = &x; } return null;");

        result.Success.Should().BeFalse();
        result.NeedsApproval.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id == "WFSCRIPT102");
    }

    // ── Approval of exact code ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedCode_Compiles_AndAnyEditNeedsApprovalAgain()
    {
        var store = new ScriptApprovalStore();
        var body = "return System.IO.File.Exists(\"/tmp/flag\");";

        var denied = Compile(body, store);
        denied.Success.Should().BeFalse();
        (await store.GetAsync(denied.CodeHash!))!.Status.Should().Be(ScriptApprovalStatus.Candidate,
            because: "a denied compile records exactly what it analyzed, for the author to request review");

        (await store.RequestAsync(denied.CodeHash!, "author", "needs a marker file")).Outcome.Should().Be(ScriptApprovalOutcome.Ok);
        (await store.DecideAsync(denied.CodeHash!, "reviewer", approve: true, "ok")).Outcome.Should().Be(ScriptApprovalOutcome.Ok);

        Compile(body, store).Success.Should().BeTrue("the reviewer approved this exact code");

        var edited = Compile(body.Replace("flag", "other", StringComparison.Ordinal), store);
        edited.Success.Should().BeFalse("an edit changes the hash, so it needs review again");
        edited.CodeHash.Should().NotBe(denied.CodeHash);
    }

    [Fact]
    public void ApprovalForAScript_DoesNotApproveTheSameTextAsATransform()
    {
        var body = "return System.IO.File.Exists(\"x\");";
        ScriptSecurityAnalyzer.CodeHash(ScriptSecurityAnalyzer.KindScript, body)
            .Should().NotBe(ScriptSecurityAnalyzer.CodeHash(ScriptSecurityAnalyzer.KindTransform, body));
    }

    [Fact]
    public void CodeHash_IgnoresLineEndingsAndTrailingWhitespace_Only()
    {
        var kind = ScriptSecurityAnalyzer.KindScript;
        ScriptSecurityAnalyzer.CodeHash(kind, "a\r\nb  \n").Should().Be(ScriptSecurityAnalyzer.CodeHash(kind, "a\nb"));
        ScriptSecurityAnalyzer.CodeHash(kind, "a\nb").Should().NotBe(ScriptSecurityAnalyzer.CodeHash(kind, "a\n b"));
    }

    [Fact]
    public async Task Author_CannotApproveTheirOwnRequest()
    {
        var store = new ScriptApprovalStore();
        var denied = Compile("return System.IO.File.Exists(\"x\");", store);
        await store.RequestAsync(denied.CodeHash!, "alice", null);

        var result = await store.DecideAsync(denied.CodeHash!, "alice", approve: true, null);

        result.Outcome.Should().Be(ScriptApprovalOutcome.SelfApproval);
        store.IsApproved(denied.CodeHash!).Should().BeFalse();
    }

    [Fact]
    public async Task OnlyRequestedCode_CanBeDecided_AndRejectedCodeCanBeRequestedAgain()
    {
        var store = new ScriptApprovalStore();
        var hash = Compile("return System.IO.File.Exists(\"x\");", store).CodeHash!;

        (await store.DecideAsync(hash, "bob", approve: true, null)).Outcome.Should().Be(ScriptApprovalOutcome.InvalidState,
            because: "a candidate nobody asked about can't be approved");

        await store.RequestAsync(hash, "alice", null);
        (await store.DecideAsync(hash, "bob", approve: false, "use the HTTP module")).Record!.Status.Should().Be(ScriptApprovalStatus.Rejected);
        store.IsApproved(hash).Should().BeFalse();

        var again = await store.RequestAsync(hash, "alice", "now justified");
        again.Outcome.Should().Be(ScriptApprovalOutcome.Ok);
        again.Record!.DecidedBy.Should().BeNull("re-requesting clears the previous decision");
    }

    [Fact]
    public async Task Catalog_SurvivesARestart_ThroughPersistence()
    {
        var persistence = new MemoryPersistence();
        var first = new ScriptApprovalStore(persistence);
        var hash = Compile("return System.IO.File.Exists(\"x\");", first).CodeHash!;
        await first.RequestAsync(hash, "alice", null);
        await first.DecideAsync(hash, "bob", approve: true, null);

        var restarted = new ScriptApprovalStore(persistence);

        restarted.IsApproved(hash).Should().BeTrue();
        (await restarted.GetAsync(hash))!.Flags.Should().NotBeEmpty();
    }

    [Fact]
    public void NoGate_DeniesEverythingFlagged()
    {
        new RoslynScriptCompiler().Compile(Request("return System.IO.File.Exists(\"x\");")).Success.Should().BeFalse();
    }

    // ── LINQ compiler: same check (it used to be a separate blocklist copy) ──────────────────────

    [Fact]
    public async Task Linq_ReflectionTypeResolution_IsDenied()
    {
        var result = await Linq("var t = System.Type.GetType(\"System.Text.StringBuilder\"); return t != null;");

        result.Success.Should().BeFalse("reflection in a LINQ body bypasses the name blocklist too (§9.2 mirror)");
        result.NeedsApproval.Should().BeTrue();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ100");
    }

    [Fact]
    public async Task Linq_ApprovedCode_Compiles()
    {
        var store = new ScriptApprovalStore();
        const string body = "var t = System.Type.GetType(\"System.Text.StringBuilder\"); return t != null;";
        var hash = (await Linq(body, store)).CodeHash!;
        await store.RequestAsync(hash, "alice", null);
        await store.DecideAsync(hash, "bob", approve: true, null);

        var result = await Linq(body, store);

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(e => e.Message)));
    }

    [Fact]
    public async Task Linq_NormalQuery_HasNoFlags()
    {
        var result = await Linq("return db.Orders.Where(o => o.total > 0m).Select(o => o.id).ToList();");

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors.Select(e => e.Message)));
        result.SecurityFlags.Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static ScriptCompileResult Compile(string body, IScriptApprovalGate? gate = null)
        => new RoslynScriptCompiler(gate).Compile(Request(body));

    private static ScriptCompileRequest Request(string body)
    {
        var source =
            "using System;\nusing System.Linq;\nusing System.Collections.Generic;\nusing System.Threading;\nusing System.Threading.Tasks;\n" +
            "namespace WorkflowRuntime {\n public static class TransformScript {\n" +
            "  public static async Task<object?> ExecuteAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> inputs, CancellationToken ct) {\n" +
            "   await Task.CompletedTask;\n" + body + "\n } } }";
        return new ScriptCompileRequest("A" + Guid.NewGuid().ToString("N"), source, body);
    }

    private static Task<LinqCompileResult> Linq(string body, IScriptApprovalGate? gate = null)
    {
        var orders = new WorkflowTableMetadata(
            ConnectionId: "conn",
            TableName: "Orders",
            Schema: null,
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("total", "numeric", false),
            },
            ClrTypeName: null,
            AssemblyName: null);
        var schema = new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        var compiler = new WorkflowLinqCompiler(new TableTypeResolver(), approvals: gate);
        return compiler.CompileAsync(new LinqCompileRequest("def", "node", body, new[] { orders }, schema));
    }

    private sealed class MemoryPersistence : IScriptApprovalPersistence
    {
        private string? json;

        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(this.json);

        public Task WriteAsync(string json, CancellationToken ct = default)
        {
            this.json = json;
            return Task.CompletedTask;
        }
    }
}
