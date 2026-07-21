// <copyright file="CSharpScriptExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Executors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Execution;

/// <summary>
/// 🟪 Phase 3.1.2 — C# script executor adapting the existing Roslyn scripting core (compiler +
/// forbidden-syntax walker + collectible runner + compiled cache) to the <see cref="IScriptExecutor"/>
/// seam (D3). Stays in the Roslyn-quarantined project so the heavy dependency remains opt-in~ ✨.
/// </summary>
public sealed class CSharpScriptExecutor : IScriptExecutor
{
    private const string RuntimeNamespace = "WorkflowRuntime";
    private const string EntryType = RuntimeNamespace + ".WorkflowScript";
    private const string EntryMethod = "ExecuteAsync";

    private readonly IRoslynScriptCompiler compiler;
    private readonly CollectibleScriptRunner runner;
    private readonly ILogger<CSharpScriptExecutor> logger;
    private readonly Dictionary<string, byte[]> compileCache = new(StringComparer.Ordinal);
    private readonly object cacheGate = new();

    /// <summary>Initializes a new instance of the <see cref="CSharpScriptExecutor"/> class~ 🟪.</summary>
    /// <param name="compiler">The shared Roslyn compiler.</param>
    /// <param name="runner">The collectible script runner.</param>
    /// <param name="logger">Optional logger.</param>
    public CSharpScriptExecutor(
        IRoslynScriptCompiler compiler,
        CollectibleScriptRunner runner,
        ILogger<CSharpScriptExecutor>? logger = null)
    {
        this.compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
        this.logger = logger ?? NullLogger<CSharpScriptExecutor>.Instance;
    }

    /// <inheritdoc/>
    public string LanguageId => "csharp";

    /// <inheritdoc/>
    public string DisplayName => "C#";

    /// <inheritdoc/>
    public async Task<ScriptExecutionResult> ExecuteAsync(
        string code,
        ScriptExecutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(context);

        var sw = Stopwatch.StartNew();

        // Prepend libraries as additional members in dependency order (source inclusion, D9)~
        var body = BuildBody(code, context.Libraries);
        var key = HashKey(body);

        byte[]? bytes;
        lock (this.cacheGate)
        {
            this.compileCache.TryGetValue(key, out bytes);
        }

        if (bytes is null)
        {
            var source = GenerateSource(body);
            var apiReference = MetadataReference.CreateFromFile(typeof(IWorkflowScriptApi).Assembly.Location);
            var result = this.compiler.Compile(new ScriptCompileRequest(
                AssemblyName: "WorkflowScript_" + Guid.NewGuid().ToString("N"),
                GeneratedSource: source,
                UserBody: body,
                ExtraReferences: new[] { apiReference }));

            if (!result.Success || result.AssemblyBytes is null)
            {
                sw.Stop();
                var errors = string.Join("; ", result.Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error).Select(d => $"({d.Line},{d.Column}) {d.Message}"));
                return ScriptExecutionResult.Fail($"C# compile failed: {errors}", context.Api.GetLogs(), sw.Elapsed);
            }

            bytes = result.AssemblyBytes;
            lock (this.cacheGate)
            {
                this.compileCache[key] = bytes;
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.Config.TimeoutSeconds));

        try
        {
            var raw = await this.runner.RunAsync(
                key,
                bytes,
                EntryType,
                EntryMethod,
                new object?[] { context.Inputs, context.Api, context.Variables, timeoutCts.Token }).ConfigureAwait(false);

            sw.Stop();
            return ScriptExecutionResult.Ok(raw, context.Api.GetVariableUpdates(), context.Api.GetLogs(), sw.Elapsed);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            sw.Stop();
            return ScriptExecutionResult.Fail($"Script timed out after {context.Config.TimeoutSeconds}s.", context.Api.GetLogs(), sw.Elapsed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            var inner = ex.InnerException ?? ex;
            this.logger.LogDebug(ex, "🟪 C# script execution failed for node {NodeId}~", context.NodeId);
            return ScriptExecutionResult.Fail($"C# error: {inner.Message}", context.Api.GetLogs(), sw.Elapsed);
        }
    }

    private static string BuildBody(string code, IReadOnlyList<ScriptLibrarySource> libraries)
    {
        if (libraries.Count == 0)
        {
            return code;
        }

        var sb = new StringBuilder();
        foreach (var lib in libraries)
        {
            sb.AppendLine($"// library: {lib.LibraryId}");
            sb.AppendLine(lib.Code);
        }

        sb.AppendLine(code);
        return sb.ToString();
    }

    private static string GenerateSource(string userBody)
        => "#nullable enable\n" +
           "using System;\n" +
           "using System.Linq;\n" +
           "using System.Collections.Generic;\n" +
           "using System.Text;\n" +
           "using System.Globalization;\n" +
           "using System.Threading;\n" +
           "using System.Threading.Tasks;\n" +
           "using Workflow.Scripting.Abstractions;\n" +
           "namespace " + RuntimeNamespace + " {\n" +
           "  public static class WorkflowScript {\n" +
           "    public static async Task<object?> ExecuteAsync(\n" +
           "        IReadOnlyDictionary<string, object?> input,\n" +
           "        IWorkflowScriptApi workflow,\n" +
           "        IReadOnlyDictionary<string, object?> variables,\n" +
           "        CancellationToken ct) {\n" +
           "      await Task.CompletedTask;\n" +
           "#line 1\n" +
           userBody + "\n" +
           "    }\n" +
           "  }\n" +
           "}\n";

    private static string HashKey(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("csharp:" + body)));
}
