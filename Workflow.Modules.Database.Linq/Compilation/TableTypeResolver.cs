// <copyright file="TableTypeResolver.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;

/// <summary>
/// 🧩 The outcome of resolving one <see cref="WorkflowTableMetadata"/> to a CLR entity type~ ✨.
/// </summary>
/// <param name="ContextPropertyName">The <c>db.X</c> property name emitted on <c>DynamicWorkflowContext</c>.</param>
/// <param name="EntityTypeName">The C# type name for <c>ITable&lt;T&gt;</c> (plugin FQN or generated POCO name).</param>
/// <param name="GeneratedPocoSource">Generated POCO class source (null for the plugin path).</param>
/// <param name="PluginAssemblyLocation">Plugin assembly file location to reference (null for the generated path).</param>
/// <param name="Diagnostics">Warnings/errors raised while resolving this table.</param>
public sealed record ResolvedTable(
    string ContextPropertyName,
    string? EntityTypeName,
    string? GeneratedPocoSource,
    string? PluginAssemblyLocation,
    IReadOnlyList<LinqDiagnostic> Diagnostics);

/// <summary>
/// 🧩 Resolves each selected table to a CLR entity type via one of two strategies (2.4.b.1 dual-POCO)~ 💖.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precedence:</b> a plugin POCO (<see cref="WorkflowTableMetadata.ClrTypeName"/>) wins when present
/// and loadable — it's authoritative (honours the author's attributes/relations). Otherwise a POCO is
/// generated from <see cref="WorkflowTableMetadata.Columns"/>. A table with neither is an error~ 🌸.
/// </para>
/// </remarks>
public sealed class TableTypeResolver
{
    /// <summary>Prefix for generated POCO type names (keeps them distinct from context property names)~.</summary>
    public const string GeneratedTypePrefix = "Gen_";

    /// <summary>
    /// Resolves a single table to its entity type~ 🎯.
    /// </summary>
    /// <param name="table">The table metadata.</param>
    /// <param name="strict">When true, unmapped column types are errors instead of warnings.</param>
    /// <returns>The resolved table (may carry error diagnostics + a null <see cref="ResolvedTable.EntityTypeName"/>).</returns>
    public ResolvedTable Resolve(WorkflowTableMetadata table, bool strict)
    {
        ArgumentNullException.ThrowIfNull(table);

        var propertyName = CodeIdentifiers.Sanitize(table.TableName, "Table");

        // ── Strategy A: plugin POCO (preferred when a CLR type is declared) ──────────────
        if (!string.IsNullOrWhiteSpace(table.ClrTypeName))
        {
            return ResolvePluginType(table, propertyName);
        }

        // ── Strategy B: generate a POCO from column metadata ─────────────────────────────
        if (table.Columns is { Count: > 0 })
        {
            return GeneratePoco(table, propertyName, strict);
        }

        // ── Neither → error ──────────────────────────────────────────────────────────────
        return new ResolvedTable(
            propertyName,
            EntityTypeName: null,
            GeneratedPocoSource: null,
            PluginAssemblyLocation: null,
            Diagnostics: new[]
            {
                new LinqDiagnostic(
                    "WFLINQ001",
                    LinqDiagnosticSeverity.Error,
                    $"Table '{table.TableName}' has neither a ClrTypeName (plugin POCO) nor Columns "
                    + "(for a generated POCO) — cannot resolve a typed entity~ 💔"),
            });
    }

    private static ResolvedTable ResolvePluginType(WorkflowTableMetadata table, string propertyName)
    {
        var type = LoadPluginType(table.ClrTypeName!, table.AssemblyName);
        if (type is null)
        {
            return new ResolvedTable(
                propertyName,
                EntityTypeName: null,
                GeneratedPocoSource: null,
                PluginAssemblyLocation: null,
                Diagnostics: new[]
                {
                    new LinqDiagnostic(
                        "WFLINQ002",
                        LinqDiagnosticSeverity.Error,
                        $"Plugin type '{table.ClrTypeName}' for table '{table.TableName}' could not be "
                        + $"loaded (assembly '{table.AssemblyName ?? "<unspecified>"}')~ 💔"),
                });
        }

        return new ResolvedTable(
            propertyName,
            EntityTypeName: "global::" + type.FullName,
            GeneratedPocoSource: null,
            PluginAssemblyLocation: type.Assembly.Location,
            Diagnostics: Array.Empty<LinqDiagnostic>());
    }

    private static Type? LoadPluginType(string clrTypeName, string? assemblyName)
    {
        // Prefer the named assembly; fall back to a scan of already-loaded assemblies.
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            try
            {
                var asm = Assembly.Load(new AssemblyName(assemblyName));
                var t = asm.GetType(clrTypeName, throwOnError: false);
                if (t is not null)
                {
                    return t;
                }
            }
            catch (Exception ex) when (ex is System.IO.FileNotFoundException or BadImageFormatException or FileLoadException)
            {
                // fall through to the scan
            }
        }

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType(clrTypeName, throwOnError: false))
            .FirstOrDefault(t => t is not null);
    }

    private static ResolvedTable GeneratePoco(WorkflowTableMetadata table, string propertyName, bool strict)
    {
        var typeName = GeneratedTypePrefix + propertyName;
        var diagnostics = new List<LinqDiagnostic>();
        var sb = new StringBuilder();

        var tableAttrName = string.IsNullOrWhiteSpace(table.Schema)
            ? table.TableName
            : table.Schema + "." + table.TableName;

        sb.AppendLine($"[global::LinqToDB.Mapping.Table(Name = \"{CodeIdentifiers.EscapeLiteral(tableAttrName)}\")]");
        sb.AppendLine($"public sealed class {typeName}");
        sb.AppendLine("{");

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var col in table.Columns!)
        {
            var propName = CodeIdentifiers.Sanitize(col.Name, "Column");
            while (!usedNames.Add(propName))
            {
                propName += "_";
            }

            if (!SqlTypeMapper.TryMap(col.DataType, col.Nullable, out var csType))
            {
                var severity = strict ? LinqDiagnosticSeverity.Error : LinqDiagnosticSeverity.Warning;
                diagnostics.Add(new LinqDiagnostic(
                    "WFLINQ003",
                    severity,
                    $"Column '{table.TableName}.{col.Name}' has unmapped SQL type '{col.DataType}' — "
                    + $"emitting as object? (limited Roslyn validation)~ ⚠️"));
            }

            sb.AppendLine($"    [global::LinqToDB.Mapping.Column(Name = \"{CodeIdentifiers.EscapeLiteral(col.Name)}\")]");
            sb.AppendLine($"    public {csType} {propName} {{ get; set; }} = default!;");
        }

        sb.AppendLine("}");

        return new ResolvedTable(
            propertyName,
            EntityTypeName: typeName,
            GeneratedPocoSource: sb.ToString(),
            PluginAssemblyLocation: null,
            Diagnostics: diagnostics);
    }
}

