// <copyright file="WorkflowLinqCompiler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🧬 Roslyn compile pipeline for typed linq node bodies (2.4.b.1)~ ✨💖.
/// </summary>
/// <remarks>
/// CopilotNote: Pure/testable — emits assembly bytes + diagnostics only. Blob caching + HMAC signing
/// are 2.4.b.2; collectible-ALC execution is 2.4.b.3. Security is enforced by the codegen-controlled
/// using allowlist + <see cref="ForbiddenSyntaxWalker"/> over the deterministic reference set~ 🌸.
/// </remarks>
public sealed class WorkflowLinqCompiler : IWorkflowLinqCompiler
{
    private const string RuntimeNamespace = "WorkflowRuntime";

    private readonly TableTypeResolver tableResolver;
    private readonly ILogger<WorkflowLinqCompiler> logger;

    /// <summary>Initializes a new instance of the <see cref="WorkflowLinqCompiler"/> class~ 🧬.</summary>
    /// <param name="tableResolver">The dual-POCO table type resolver.</param>
    /// <param name="logger">Logger (optional).</param>
    public WorkflowLinqCompiler(TableTypeResolver tableResolver, ILogger<WorkflowLinqCompiler>? logger = null)
    {
        this.tableResolver = tableResolver ?? throw new ArgumentNullException(nameof(tableResolver));
        this.logger = logger ?? NullLogger<WorkflowLinqCompiler>.Instance;
    }

    /// <inheritdoc/>
    public Task<LinqCompileResult> CompileAsync(LinqCompileRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<LinqDiagnostic>();
        var warnings = new List<LinqDiagnostic>();

        // 1️⃣ Security blocklist over the raw user body (mitigates C1)~ 🛡️
        var forbidden = ForbiddenSyntaxWalker.Scan(request.UserCodeBody);
        if (forbidden.Count > 0)
        {
            // A forbidden-syntax body never gets compiled — fail fast with the violations~
            return Task.FromResult(LinqCompileResult.Fail(forbidden));
        }

        // 2️⃣ Resolve every selected table (plugin POCO or column-generated POCO)~ 🧩
        var resolved = new List<ResolvedTable>(request.SelectedTables.Count);
        foreach (var table in request.SelectedTables)
        {
            var r = this.tableResolver.Resolve(table, request.StrictTypeMode);
            resolved.Add(r);
            Partition(r.Diagnostics, errors, warnings);
        }

        // 3️⃣ Generate the LinqInputs accessor struct~ 🧬
        var inputsSource = LinqInputsCodeGenerator.Generate(request.InputSchema, request.StrictTypeMode, out var inputDiags);
        Partition(inputDiags, errors, warnings);

        // If codegen can't produce valid types (unresolved tables / strict-mode failures), stop here~
        if (errors.Count > 0)
        {
            return Task.FromResult(LinqCompileResult.Fail(errors, warnings));
        }

        // 4️⃣ Assemble the full compilation unit~ 📝
        var source = BuildSource(resolved, inputsSource, request.UserCodeBody);
        var pluginLocations = resolved
            .Where(r => r.PluginAssemblyLocation is not null)
            .Select(r => r.PluginAssemblyLocation!);

        // 5️⃣ Compile + emit~ 🧠
        var assemblyName = $"Workflow_Linq_{Sanitize(request.DefinitionId)}_{Sanitize(request.NodeId)}_{Guid.NewGuid():N}";
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest),
            cancellationToken: ct);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            ReferenceWhitelist.Build(pluginLocations),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms, cancellationToken: ct);

        foreach (var d in emit.Diagnostics)
        {
            switch (d.Severity)
            {
                case DiagnosticSeverity.Error:
                    errors.Add(ToDiagnostic(d, LinqDiagnosticSeverity.Error));
                    break;
                case DiagnosticSeverity.Warning:
                    warnings.Add(ToDiagnostic(d, LinqDiagnosticSeverity.Warning));
                    break;
            }
        }

        if (!emit.Success || errors.Count > 0)
        {
            this.logger.LogDebug(
                "Linq compile failed for {DefinitionId}/{NodeId}: {ErrorCount} error(s)~",
                request.DefinitionId,
                request.NodeId,
                errors.Count);
            return Task.FromResult(LinqCompileResult.Fail(errors, warnings));
        }

        return Task.FromResult(new LinqCompileResult(true, ms.ToArray(), errors, warnings));
    }

    private static string BuildSource(IReadOnlyList<ResolvedTable> tables, string inputsSource, string userBody)
    {
        var sb = new StringBuilder();
        foreach (var u in ReferenceWhitelist.Usings)
        {
            sb.AppendLine($"using {u};");
        }

        sb.AppendLine();
        sb.AppendLine($"namespace {RuntimeNamespace}");
        sb.AppendLine("{");
        sb.AppendLine(DynamicContextCodeGenerator.GeneratePocos(tables));
        sb.AppendLine(DynamicContextCodeGenerator.GenerateContext(tables));
        sb.AppendLine(inputsSource);
        sb.AppendLine(DynamicContextCodeGenerator.GenerateWrapper(userBody));
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Partition(
        IReadOnlyList<LinqDiagnostic> source,
        List<LinqDiagnostic> errors,
        List<LinqDiagnostic> warnings)
    {
        foreach (var d in source)
        {
            (d.Severity == LinqDiagnosticSeverity.Error ? errors : warnings).Add(d);
        }
    }

    private static LinqDiagnostic ToDiagnostic(Diagnostic d, LinqDiagnosticSeverity severity)
    {
        var pos = d.Location.GetLineSpan().StartLinePosition;
        return new LinqDiagnostic(d.Id, severity, d.GetMessage(), pos.Line + 1, pos.Character + 1);
    }

    private static string Sanitize(string value)
        => new(value.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
}

