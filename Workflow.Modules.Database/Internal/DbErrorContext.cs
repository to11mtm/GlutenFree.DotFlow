// <copyright file="DbErrorContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Data.Common;
using System.Text;

/// <summary>
/// 🚨 Extracts provider-specific error context (SQL state, constraint/column/table names)
/// from a database exception so module failures carry actionable detail~ 🌸.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.2 — reads Npgsql's <c>PostgresException</c> (rich, strongly-typed
/// fields) and Microsoft.Data.Sqlite's <c>SqliteException</c> (constraint info lives in the
/// message). We read the Postgres fields reflectively so this helper doesn't hard-depend on a
/// specific Npgsql surface — falling back gracefully to the exception message everywhere else~ 💖.
/// </remarks>
public static class DbErrorContext
{
    /// <summary>
    /// Builds a human-readable error string enriched with any available constraint context~ 📝.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <returns>A message including SQL state / constraint / column when the provider exposes them.</returns>
    public static string Describe(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var sb = new StringBuilder(ex.Message);

        // 🐘 Postgres — Npgsql.PostgresException exposes SqlState/ConstraintName/ColumnName/TableName.
        // Read reflectively so we don't force a compile-time Npgsql type dependency here~
        var typeName = ex.GetType().FullName;
        if (typeName == "Npgsql.PostgresException")
        {
            AppendProp(sb, ex, "SqlState", "sqlState");
            AppendProp(sb, ex, "ConstraintName", "constraint");
            AppendProp(sb, ex, "ColumnName", "column");
            AppendProp(sb, ex, "TableName", "table");
        }
        else if (ex is DbException dbEx && dbEx.SqlState is { Length: > 0 } state)
        {
            sb.Append(" [sqlState=").Append(state).Append(']');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a five-character SQLSTATE code from a <see cref="DbException"/> when present~ 🔢.
    /// </summary>
    /// <param name="ex">The caught exception.</param>
    /// <returns>The SQLSTATE, or <see langword="null"/> when unavailable.</returns>
    public static string? TryGetSqlState(Exception ex)
        => ex is DbException dbEx ? dbEx.SqlState : null;

    private static void AppendProp(StringBuilder sb, Exception ex, string propertyName, string label)
    {
        var value = ex.GetType().GetProperty(propertyName)?.GetValue(ex) as string;
        if (!string.IsNullOrEmpty(value))
        {
            sb.Append(" [").Append(label).Append('=').Append(value).Append(']');
        }
    }
}
