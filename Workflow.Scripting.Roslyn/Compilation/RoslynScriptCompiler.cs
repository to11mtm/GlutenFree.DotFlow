// <copyright file="RoslynScriptCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Compilation;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Workflow.Scripting.Approvals;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Security;

/// <summary>
/// 🧬 Default <see cref="IRoslynScriptCompiler"/> — the shared, domain-agnostic compile pipeline~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: (1) security-walk the user body standalone (<see cref="ForbiddenSyntaxWalker"/>) —
/// unsafe/pointers/stackalloc are hard errors, (2) parse the caller's generated source, (3) compile
/// against the deterministic .NET 8 BCL ref set (Basic.Reference.Assemblies) plus caller-supplied
/// references, (4) §9.2: find every sensitive API the user code BINDS to
/// (<see cref="ScriptSecurityAnalyzer"/>) and refuse to emit unless a reviewer approved this exact
/// code (<see cref="IScriptApprovalGate"/>, fail closed), (5) emit bytes or map diagnostics.
/// Reference trimming can't be the gate: Type, MethodInfo and File all live in System.Runtime~ 🌸.
/// </remarks>
public sealed class RoslynScriptCompiler : IRoslynScriptCompiler
{
    /// <summary>Walker ids that stay hard errors (not approvable): unsafe, pointers, stackalloc.</summary>
    private static readonly HashSet<string> HardWalkerIds = new(System.StringComparer.Ordinal)
    {
        "WFSCRIPT102", "WFSCRIPT103", "WFSCRIPT104",
    };

    private readonly IScriptApprovalGate approvals;

    /// <summary>Initializes a new instance of the <see cref="RoslynScriptCompiler"/> class.</summary>
    /// <param name="approvals">The approval gate; null denies every flagged script (fail closed).</param>
    public RoslynScriptCompiler(IScriptApprovalGate? approvals = null)
    {
        this.approvals = approvals ?? DenyAllScriptApprovalGate.Instance;
    }

    /// <inheritdoc/>
    public ScriptCompileResult Compile(ScriptCompileRequest request)
    {
        var diagnostics = new List<ScriptDiagnostic>();

        // 1) 🛡️ Forbidden-syntax scan of the raw user body. Name/attribute hits are re-derived as
        //    reviewable flags in step 4; only unsafe/pointer/stackalloc stop us here~
        var hard = ForbiddenSyntaxWalker.Scan(request.UserBody).Where(v => HardWalkerIds.Contains(v.Id)).ToList();
        if (hard.Count > 0)
        {
            return new ScriptCompileResult(false, null, hard);
        }

        // 2) Parse the generated source~
        var tree = CSharpSyntaxTree.ParseText(request.GeneratedSource);

        // 3) Reference set: deterministic BCL + extras~ 📚
        var references = new List<MetadataReference>();
        references.AddRange(Basic.Reference.Assemblies.Net80.References.All);
        if (request.ExtraReferences is not null)
        {
            references.AddRange(request.ExtraReferences);
        }

        var compilation = CSharpCompilation.Create(
            request.AssemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false));

        // 4) 🚩 §9.2: semantic check of the user body + reviewer approval of this exact code~
        var flags = ScriptSecurityAnalyzer.Analyze(compilation, tree, request.UserBody);
        string? codeHash = null;
        if (flags.Count > 0)
        {
            codeHash = ScriptSecurityAnalyzer.CodeHash(request.Kind, request.UserBody);
            if (!this.approvals.IsApproved(codeHash))
            {
                this.approvals.RecordCandidate(codeHash, request.Kind, request.UserBody, flags);
                var denied = flags
                    .Select(f => new ScriptDiagnostic("WFSCRIPT100", ScriptDiagnosticSeverity.Error, ScriptSecurityAnalyzer.DeniedMessage(f, codeHash), f.Line, f.Column))
                    .ToList();
                return new ScriptCompileResult(false, null, denied, flags, codeHash, NeedsApproval: true);
            }
        }

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        foreach (var d in emitResult.Diagnostics)
        {
            if (d.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            var severity = d.Severity switch
            {
                DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => request.StrictWarnings ? ScriptDiagnosticSeverity.Error : ScriptDiagnosticSeverity.Warning,
                _ => ScriptDiagnosticSeverity.Info,
            };

            var pos = d.Location.GetLineSpan().StartLinePosition;
            diagnostics.Add(new ScriptDiagnostic(d.Id, severity, d.GetMessage(), pos.Line + 1, pos.Character + 1));
        }

        var hasError = diagnostics.Any(x => x.Severity == ScriptDiagnosticSeverity.Error);
        if (!emitResult.Success || hasError)
        {
            return new ScriptCompileResult(false, null, diagnostics, flags, codeHash);
        }

        return new ScriptCompileResult(true, ms.ToArray(), diagnostics, flags, codeHash);
    }
}
