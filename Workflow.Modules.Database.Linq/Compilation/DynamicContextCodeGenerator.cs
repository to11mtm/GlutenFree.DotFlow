// <copyright file="DynamicContextCodeGenerator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 🧬 Emits <c>DynamicWorkflowContext</c> (a <c>DataConnection</c> with one <c>ITable&lt;T&gt;</c> per
/// selected table), the column-generated POCOs, and the <c>WorkflowScript</c> wrapper method~ ✨.
/// </summary>
public static class DynamicContextCodeGenerator
{
    /// <summary>The namespace the generated compilation unit lives in~ 📦.</summary>
    public const string RuntimeNamespace = "WorkflowRuntime";

    /// <summary>Names the codegen owns — a table alias must never shadow them~ 🚧.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "DynamicWorkflowContext",
        "WorkflowScript",
        "LinqInputs",
    };

    /// <summary>
    /// Emits <c>using Table = Gen_Table;</c> alias directives so user code can name a table's row
    /// type by the table's own name (<c>new FooBar { … }</c>) instead of the internal
    /// <c>Gen_FooBar</c>/plugin FQN~ 🏷️.
    /// </summary>
    /// <param name="tables">The resolved tables.</param>
    /// <returns>The alias directive source (empty when nothing can be aliased).</returns>
    public static string GenerateEntityAliases(IReadOnlyList<ResolvedTable> tables)
    {
        var sb = new StringBuilder();
        foreach (var alias in EntityAliases(tables))
        {
            sb.AppendLine($"    using {alias.Key} = {alias.Value};");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The alias → entity type map actually emitted (alias name → fully qualified/generated type)~ 🗺️.
    /// </summary>
    /// <param name="tables">The resolved tables.</param>
    /// <returns>An ordered alias map; tables whose name already equals their type are skipped.</returns>
    public static IReadOnlyDictionary<string, string> EntityAliases(IReadOnlyList<ResolvedTable> tables)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var t in tables)
        {
            if (t.EntityTypeName is null ||
                string.Equals(t.ContextPropertyName, t.EntityTypeName, StringComparison.Ordinal) ||
                ReservedNames.Contains(t.ContextPropertyName) ||
                map.ContainsKey(t.ContextPropertyName))
            {
                continue;
            }

            // Generated POCOs live in the runtime namespace; plugin types are already global::-qualified.
            var target = t.EntityTypeName.StartsWith("global::", StringComparison.Ordinal)
                ? t.EntityTypeName
                : "global::" + RuntimeNamespace + "." + t.EntityTypeName;
            map[t.ContextPropertyName] = target;
        }

        return map;
    }

    /// <summary>Concatenates the generated-POCO class sources (empty for all-plugin tables)~ 🧩.</summary>
    /// <param name="tables">The resolved tables.</param>
    /// <returns>The POCO class sources.</returns>
    public static string GeneratePocos(IReadOnlyList<ResolvedTable> tables)
    {
        var sb = new StringBuilder();
        foreach (var t in tables)
        {
            if (t.GeneratedPocoSource is not null)
            {
                sb.AppendLine(t.GeneratedPocoSource);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits the <c>DynamicWorkflowContext</c> class with a typed table property per resolved table~ 🔌.
    /// </summary>
    /// <param name="tables">The resolved tables (only those with a non-null entity type are emitted).</param>
    /// <returns>The context class source.</returns>
    public static string GenerateContext(IReadOnlyList<ResolvedTable> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("public sealed class DynamicWorkflowContext : global::LinqToDB.Data.DataConnection");
        sb.AppendLine("{");
        sb.AppendLine("    public DynamicWorkflowContext(global::LinqToDB.DataOptions options) : base(options) { }");
        sb.AppendLine();

        foreach (var t in tables)
        {
            if (t.EntityTypeName is null)
            {
                continue;
            }

            sb.AppendLine(
                $"    public global::LinqToDB.ITable<{t.EntityTypeName}> {t.ContextPropertyName} "
                + $"=> this.GetTable<{t.EntityTypeName}>();");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Emits the <c>WorkflowScript.ExecuteAsync(db, inputs, ct)</c> wrapper around the user's body~ 🚀.
    /// </summary>
    /// <param name="userCodeBody">The raw user method body.</param>
    /// <returns>The wrapper class source.</returns>
    public static string GenerateWrapper(string userCodeBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("public sealed class WorkflowScript");
        sb.AppendLine("{");
        sb.AppendLine("    public async global::System.Threading.Tasks.Task<object?> ExecuteAsync(");
        sb.AppendLine("        DynamicWorkflowContext db,");
        sb.AppendLine("        LinqInputs inputs,");
        sb.AppendLine("        global::System.Threading.CancellationToken ct)");
        sb.AppendLine("    {");
        sb.AppendLine("        await global::System.Threading.Tasks.Task.CompletedTask;");
        sb.AppendLine("        // ─── USER CODE BEGINS ───");
        sb.AppendLine(userCodeBody);
        sb.AppendLine("        // ─── USER CODE ENDS ───");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

