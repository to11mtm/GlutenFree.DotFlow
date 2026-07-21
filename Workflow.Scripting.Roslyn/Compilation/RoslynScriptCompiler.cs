// <copyright file="RoslynScriptCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Scripting.Roslyn.Compilation;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Workflow.Scripting.Roslyn.Abstractions;

/// <summary>
/// 🧬 Default <see cref="IRoslynScriptCompiler"/> — the shared, domain-agnostic compile pipeline~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: (1) security-walk the user body standalone (<see cref="ForbiddenSyntaxWalker"/>),
/// (2) parse the caller's generated source, (3) compile against the deterministic .NET 8 BCL ref set
/// (Basic.Reference.Assemblies) plus caller-supplied references, (4) emit bytes or map diagnostics.
/// The security gate is the caller's usings allowlist + the walker, not reference trimming~ 🌸.
/// </remarks>
public sealed class RoslynScriptCompiler : IRoslynScriptCompiler
{
    /// <inheritdoc/>
    public ScriptCompileResult Compile(ScriptCompileRequest request)
    {
        var diagnostics = new List<ScriptDiagnostic>();

        // 1) 🛡️ Forbidden-syntax scan of the raw user body~
        var violations = ForbiddenSyntaxWalker.Scan(request.UserBody);
        if (violations.Count > 0)
        {
            return new ScriptCompileResult(false, null, violations);
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
            return new ScriptCompileResult(false, null, diagnostics);
        }

        return new ScriptCompileResult(true, ms.ToArray(), diagnostics);
    }
}
