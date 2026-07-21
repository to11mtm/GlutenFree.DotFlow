// <copyright file="TransformScriptPreviewer.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script.Compilation;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Scripting.Roslyn.Execution;

/// <summary>
/// 🔎 Default <see cref="ITransformScriptPreviewer"/> — compile-inclusive, pure in-memory run against
/// caller-supplied sample rows (Q7)~ ✨.
/// </summary>
public sealed class TransformScriptPreviewer : ITransformScriptPreviewer
{
    private readonly ITransformScriptCompiler compiler;

    /// <summary>Initializes a new instance of the <see cref="TransformScriptPreviewer"/> class~ 🔎.</summary>
    /// <param name="compiler">The transform script compiler.</param>
    public TransformScriptPreviewer(ITransformScriptCompiler compiler)
    {
        this.compiler = compiler;
    }

    /// <inheritdoc/>
    public async Task<TransformScriptPreviewResult> PreviewAsync(
        string userBody,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sampleRows,
        IReadOnlyDictionary<string, object?> sampleInputs,
        int timeoutSeconds = 5,
        CancellationToken ct = default)
    {
        var compiled = this.compiler.Compile(userBody);
        if (!compiled.Success || compiled.AssemblyBytes is null)
        {
            return new TransformScriptPreviewResult(false, null, 0, compiled.Diagnostics);
        }

        using var runner = new CollectibleScriptRunner();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            var raw = await runner.RunAsync(
                "preview",
                compiled.AssemblyBytes,
                this.compiler.EntryTypeName,
                this.compiler.EntryMethodName,
                new object?[] { sampleRows, sampleInputs, timeoutCts.Token }).ConfigureAwait(false);

            var materialized = ScriptResultMaterializer.Materialize(raw);
            sw.Stop();
            return new TransformScriptPreviewResult(true, materialized, sw.ElapsedMilliseconds, compiled.Diagnostics);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var inner = ex.InnerException ?? ex;
            var diags = new List<Workflow.Scripting.Roslyn.Abstractions.ScriptDiagnostic>(compiled.Diagnostics)
            {
                new("WFSCRIPT500", Workflow.Scripting.Roslyn.Abstractions.ScriptDiagnosticSeverity.Error, $"runtime error: {inner.Message}"),
            };
            return new TransformScriptPreviewResult(false, null, sw.ElapsedMilliseconds, diags);
        }
    }
}
