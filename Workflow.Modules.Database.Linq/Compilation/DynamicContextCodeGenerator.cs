// <copyright file="DynamicContextCodeGenerator.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Linq.Compilation;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// 🧬 Emits <c>DynamicWorkflowContext</c> (a <c>DataConnection</c> with one <c>ITable&lt;T&gt;</c> per
/// selected table), the column-generated POCOs, and the <c>WorkflowScript</c> wrapper method~ ✨.
/// </summary>
public static class DynamicContextCodeGenerator
{
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

