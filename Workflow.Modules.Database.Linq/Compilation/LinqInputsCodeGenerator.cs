// <copyright file="LinqInputsCodeGenerator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System.Collections.Generic;
using System.Text;
using Workflow.Core.Models;
using Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🧬 Emits the <c>LinqInputs</c> accessor struct from a node's <see cref="ModuleSchema.Properties"/>
/// (design doc §8.6 Phase 1)~ ✨.
/// </summary>
public static class LinqInputsCodeGenerator
{
    /// <summary>
    /// Generates the <c>LinqInputs</c> struct source + any type-mapping diagnostics~ 🎯.
    /// </summary>
    /// <param name="schema">The node input schema.</param>
    /// <param name="strict">When true, a non-allowlisted property type is an error (else a warning + <c>object?</c>).</param>
    /// <param name="diagnostics">Collected warnings/errors.</param>
    /// <returns>The generated struct source (inside the <c>WorkflowRuntime</c> namespace).</returns>
    public static string Generate(ModuleSchema schema, bool strict, out IReadOnlyList<LinqDiagnostic> diagnostics)
    {
        var diags = new List<LinqDiagnostic>();
        var sb = new StringBuilder();

        sb.AppendLine("public readonly struct LinqInputs");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly global::System.Collections.Generic.IReadOnlyDictionary<string, object?> _raw;");
        sb.AppendLine();
        sb.AppendLine("    public LinqInputs(global::System.Collections.Generic.IReadOnlyDictionary<string, object?> raw)");
        sb.AppendLine("        => _raw = raw;");
        sb.AppendLine();

        var used = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var prop in schema.Properties)
        {
            var name = CodeIdentifiers.Sanitize(prop.Name, "Input");
            while (!used.Add(name))
            {
                name += "_";
            }

            if (!RestrictedTypeMapper.TryMap(prop.DataType, out var csType))
            {
                var severity = strict ? LinqDiagnosticSeverity.Error : LinqDiagnosticSeverity.Warning;
                diags.Add(new LinqDiagnostic(
                    "WFLINQ004",
                    severity,
                    $"Input property '{prop.Name}' has non-allowlisted type "
                    + $"'{prop.DataType?.Name ?? "<null>"}' — emitting as object? "
                    + "(limited Roslyn validation)~ ⚠️"));
            }

            var key = CodeIdentifiers.EscapeLiteral(prop.Name);
            if (prop.IsRequired)
            {
                // Fail-fast: KeyNotFoundException if a required value is missing.
                sb.AppendLine($"    public {csType} {name} => ({csType})_raw[\"{key}\"]!;");
            }
            else
            {
                sb.AppendLine(
                    $"    public {csType} {name} => _raw.TryGetValue(\"{key}\", out var __v_{name}) "
                    + $"? ({csType})__v_{name}! : default({csType})!;");
            }
        }

        sb.AppendLine("}");

        diagnostics = diags;
        return sb.ToString();
    }
}

