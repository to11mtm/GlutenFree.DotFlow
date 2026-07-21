// <copyright file="TransformScriptCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Transform.Script.Compilation;

using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🧬 Default <see cref="ITransformScriptCompiler"/> — wraps the user body into a
/// <c>WorkflowRuntime.TransformScript.ExecuteAsync(rows, inputs, ct)</c> method using only BCL
/// types (no database coupling) and compiles it via the shared Roslyn core~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: The user body receives <c>rows</c> (<c>IReadOnlyList&lt;IReadOnlyDictionary&lt;string, object?&gt;&gt;</c>)
/// and <c>inputs</c> (<c>IReadOnlyDictionary&lt;string, object?&gt;</c>) and returns <c>object?</c>. Only a
/// curated set of usings is prepended (no <c>System.IO</c>/<c>System.Net</c>/reflection); the
/// <c>ForbiddenSyntaxWalker</c> blocks fully-qualified reaches~ 🌸.
/// </remarks>
public sealed class TransformScriptCompiler : ITransformScriptCompiler
{
    private const string RuntimeNamespace = "WorkflowRuntime";

    private readonly IRoslynScriptCompiler compiler;

    /// <summary>Initializes a new instance of the <see cref="TransformScriptCompiler"/> class~ 🧬.</summary>
    /// <param name="compiler">The shared Roslyn compiler.</param>
    public TransformScriptCompiler(IRoslynScriptCompiler compiler)
    {
        this.compiler = compiler;
    }

    /// <inheritdoc/>
    public string EntryTypeName => $"{RuntimeNamespace}.TransformScript";

    /// <inheritdoc/>
    public string EntryMethodName => "ExecuteAsync";

    /// <inheritdoc/>
    public int SchemaVersion => 1;

    /// <inheritdoc/>
    public TransformScriptCompileResult Compile(string userBody, bool strictWarnings = false)
    {
        var source =
            "#nullable enable\n" +
            "using System;\n" +
            "using System.Linq;\n" +
            "using System.Collections.Generic;\n" +
            "using System.Text;\n" +
            "using System.Text.RegularExpressions;\n" +
            "using System.Globalization;\n" +
            "using System.Threading;\n" +
            "using System.Threading.Tasks;\n" +
            "namespace " + RuntimeNamespace + " {\n" +
            "  public static class TransformScript {\n" +
            "    public static async Task<object?> ExecuteAsync(\n" +
            "        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,\n" +
            "        IReadOnlyDictionary<string, object?> inputs,\n" +
            "        CancellationToken ct) {\n" +
            "      await Task.CompletedTask;\n" +
            "#line 1\n" +
            userBody + "\n" +
            "    }\n" +
            "  }\n" +
            "}\n";

        var request = new ScriptCompileRequest(
            AssemblyName: "TransformScript_" + System.Guid.NewGuid().ToString("N"),
            GeneratedSource: source,
            UserBody: userBody,
            ExtraReferences: null,
            StrictWarnings: strictWarnings);

        var result = this.compiler.Compile(request);
        return new TransformScriptCompileResult(result.Success, result.AssemblyBytes, result.Diagnostics);
    }
}
