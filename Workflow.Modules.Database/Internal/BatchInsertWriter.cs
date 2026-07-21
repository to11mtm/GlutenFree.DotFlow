// <copyright file="BatchInsertWriter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using LinqToDB.Data;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 📊 Writes N row-dictionaries into a table as batched, parameterised multi-row INSERTs —
/// the <c>BulkCopyType.MultipleRows</c> SQL shape, hand-built so it works with dynamic
/// dictionaries (no typed entity, no Reflection.Emit)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Phase 2.4.a.4 — chosen over linq2db's generic <c>BulkCopy&lt;T&gt;</c> because that
/// path needs a compile-time entity type. The typed <c>AsQueryable</c>/<c>InsertWithOutput</c> route
/// belongs to 2.4.b (Roslyn-generated models). Here we stay stringly-typed (D7) but still deliver the
/// "retrieve generated columns" benefit via an optional provider-aware <c>RETURNING</c> clause~ 🌸.
/// </para>
/// <para>
/// Every value binds through <see cref="SqlParameterBinder"/> — SQL text is only ever table/column
/// identifiers + placeholders, never interpolated values.
/// </para>
/// </remarks>
public static class BatchInsertWriter
{
    // Conservative per-statement parameter caps (SQLite historically 999; Postgres 65535)~ 🛡️
    private const int SqliteParamLimit = 900;
    private const int PostgresParamLimit = 60000;
    private const int DefaultParamLimit = 900;

    /// <summary>
    /// The result of a bulk write — inserted-row count plus any RETURNING output rows~ 📊.
    /// </summary>
    /// <param name="InsertedCount">Total rows inserted.</param>
    /// <param name="OutputRows">Rows returned by a RETURNING clause (empty when none requested).</param>
    public sealed record BulkWriteResult(int InsertedCount, IReadOnlyList<IReadOnlyDictionary<string, object?>> OutputRows);

    /// <summary>
    /// Thrown when a row's value fails to bind — carries the offending row index~ 🚨.
    /// </summary>
    public sealed class BulkRowBindException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="BulkRowBindException"/> class.</summary>
        /// <param name="rowIndex">The offending row index.</param>
        /// <param name="inner">The underlying binding exception.</param>
        public BulkRowBindException(int rowIndex, Exception inner)
            : base($"row {rowIndex}: {inner.Message}", inner)
        {
            this.RowIndex = rowIndex;
        }

        /// <summary>Initializes a new instance of the <see cref="BulkRowBindException"/> class.</summary>
        public BulkRowBindException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="BulkRowBindException"/> class.</summary>
        /// <param name="message">The error message.</param>
        public BulkRowBindException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="BulkRowBindException"/> class.</summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public BulkRowBindException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>Gets the offending row index.</summary>
        public int RowIndex { get; }
    }

    /// <summary>
    /// Inserts <paramref name="data"/> into <paramref name="tableName"/> in batches~ 📊.
    /// </summary>
    /// <param name="db">The open connection (inside a transaction).</param>
    /// <param name="tableName">The target table (optionally schema-qualified).</param>
    /// <param name="data">The row dictionaries to insert.</param>
    /// <param name="columnMapping">Optional input-key → DB-column mapping; identity when null.</param>
    /// <param name="batchSize">Requested rows per statement (clamped by the provider param limit).</param>
    /// <param name="returningColumns">Optional columns to emit via a RETURNING clause.</param>
    /// <returns>The inserted-row count and any RETURNING output rows.</returns>
    /// <exception cref="BulkRowBindException">Thrown when a value can't be bound (carries row index).</exception>
    public static BulkWriteResult Write(
        DataConnection db,
        string tableName,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> data,
        IReadOnlyDictionary<string, string>? columnMapping,
        int batchSize,
        IReadOnlyList<string>? returningColumns)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(data);

        if (data.Count == 0)
        {
            return new BulkWriteResult(0, Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        // (sourceKey, dbColumn) pairs in a stable order~ 🏷️
        var columns = ResolveColumns(data, columnMapping);
        if (columns.Count == 0)
        {
            return new BulkWriteResult(0, Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        var hasReturning = returningColumns is { Count: > 0 };
        var returningClause = hasReturning
            ? " RETURNING " + string.Join(", ", QuoteAll(returningColumns!))
            : string.Empty;

        var rowsPerStatement = ComputeRowsPerStatement(db.DataProvider.Name, batchSize, columns.Count);

        var insertColumnList = string.Join(", ", QuoteAll(ColumnNames(columns)));
        var insertPrefix = $"INSERT INTO {tableName} ({insertColumnList}) VALUES ";

        var insertedCount = 0;
        var outputRows = new List<IReadOnlyDictionary<string, object?>>();

        for (var start = 0; start < data.Count; start += rowsPerStatement)
        {
            var end = Math.Min(start + rowsPerStatement, data.Count);
            var (sql, parameters) = BuildBatch(insertPrefix, returningClause, columns, data, start, end);

            if (hasReturning)
            {
                using var reader = db.ExecuteReader(sql, parameters);
                var r = reader.Reader!;
                var fieldCount = r.FieldCount;
                var names = new string[fieldCount];
                for (var i = 0; i < fieldCount; i++)
                {
                    names[i] = r.GetName(i);
                }

                while (r.Read())
                {
                    var row = new Dictionary<string, object?>(fieldCount, StringComparer.Ordinal);
                    for (var i = 0; i < fieldCount; i++)
                    {
                        var value = r.GetValue(i);
                        row[names[i]] = value is DBNull ? null : value;
                    }

                    outputRows.Add(row);
                    insertedCount++;
                }
            }
            else
            {
                insertedCount += db.Execute(sql, parameters);
            }
        }

        return new BulkWriteResult(insertedCount, outputRows);
    }

    /// <summary>Resolves the ordered (sourceKey, dbColumn) list from the mapping or the row-key union~ 🏷️.</summary>
    private static IReadOnlyList<(string SourceKey, string Column)> ResolveColumns(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> data,
        IReadOnlyDictionary<string, string>? columnMapping)
    {
        if (columnMapping is { Count: > 0 })
        {
            var mapped = new List<(string, string)>(columnMapping.Count);
            foreach (var kv in columnMapping)
            {
                mapped.Add((kv.Key, kv.Value));
            }

            return mapped;
        }

        // Identity: union of keys across all rows, ordered by first appearance~
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<(string, string)>();
        foreach (var row in data)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                {
                    columns.Add((key, key));
                }
            }
        }

        return columns;
    }

    /// <summary>Builds one multi-row INSERT statement + its bound parameters for rows [start, end)~ 🧷.</summary>
    private static (string Sql, DataParameter[] Parameters) BuildBatch(
        string insertPrefix,
        string returningClause,
        IReadOnlyList<(string SourceKey, string Column)> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> data,
        int start,
        int end)
    {
        var sb = new StringBuilder(insertPrefix);
        var parameters = new List<DataParameter>((end - start) * columns.Count);

        for (var rowIndex = start; rowIndex < end; rowIndex++)
        {
            if (rowIndex > start)
            {
                sb.Append(", ");
            }

            sb.Append('(');
            var row = data[rowIndex];
            for (var colIndex = 0; colIndex < columns.Count; colIndex++)
            {
                if (colIndex > 0)
                {
                    sb.Append(", ");
                }

                var paramName = $"p{rowIndex}_{colIndex}";
                sb.Append('@').Append(paramName);

                row.TryGetValue(columns[colIndex].SourceKey, out var value);
                try
                {
                    parameters.Add(SqlParameterBinder.BindOne(paramName, value));
                }
                catch (SqlParameterBindingException ex)
                {
                    throw new BulkRowBindException(rowIndex, ex);
                }
            }

            sb.Append(')');
        }

        sb.Append(returningClause);
        return (sb.ToString(), parameters.ToArray());
    }

    private static int ComputeRowsPerStatement(string providerName, int batchSize, int columnCount)
    {
        var paramLimit = providerName.Contains("SQLite", StringComparison.OrdinalIgnoreCase)
            ? SqliteParamLimit
            : providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? PostgresParamLimit
                : DefaultParamLimit;

        var byParams = Math.Max(1, paramLimit / Math.Max(1, columnCount));
        var requested = batchSize > 0 ? batchSize : 1;
        return Math.Max(1, Math.Min(requested, byParams));
    }

    private static IEnumerable<string> ColumnNames(IReadOnlyList<(string SourceKey, string Column)> columns)
    {
        foreach (var c in columns)
        {
            yield return c.Column;
        }
    }

    /// <summary>Double-quotes identifiers (standard SQL; works on SQLite + Postgres)~ 🏷️.</summary>
    private static IEnumerable<string> QuoteAll(IEnumerable<string> identifiers)
    {
        foreach (var id in identifiers)
        {
            yield return Quote(id);
        }
    }

    private static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}



