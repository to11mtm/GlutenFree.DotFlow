// <copyright file="CatalogSchemaImporter.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Catalog;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// 📥 One-shot schema import — introspects a live connection and populates <see cref="IWorkflowTableCatalog"/>
/// (Q17/D19, 2.4.b.4)~ ✨. No Roslyn dependency, so it lives in the shared Database project.
/// </summary>
public interface ICatalogSchemaImporter
{
    /// <summary>
    /// Introspects the named connection's schema and upserts its tables into the catalog~ 🎯.
    /// </summary>
    /// <param name="connectionId">The named connection id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of tables imported.</returns>
    /// <exception cref="ConnectionNotFoundException">Thrown when the connection id is unknown/disabled.</exception>
    Task<int> ImportAsync(string connectionId, CancellationToken ct = default);
}

/// <summary>
/// 📥 Default schema importer for Postgres (<c>information_schema</c>) + SQLite (<c>PRAGMA table_info</c>)~ 💖.
/// </summary>
/// <remarks>
/// CopilotNote: Manual, on-demand, no versioning (D10 unchanged) — versioned auto-discovery is 2.4.b.P3.
/// The <c>connectionId</c>→404 mapping lives at the API layer (2.4.b.5); here an unknown connection
/// surfaces as <see cref="ConnectionNotFoundException"/> from the factory~ 🌸.
/// </remarks>
public sealed class CatalogSchemaImporter : ICatalogSchemaImporter
{
    private readonly IDbConnectionFactory connectionFactory;
    private readonly IWorkflowTableCatalog catalog;

    /// <summary>Initializes a new instance of the <see cref="CatalogSchemaImporter"/> class~ 📥.</summary>
    /// <param name="connectionFactory">The connection factory.</param>
    /// <param name="catalog">The table catalog to populate.</param>
    public CatalogSchemaImporter(IDbConnectionFactory connectionFactory, IWorkflowTableCatalog catalog)
    {
        this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <inheritdoc/>
    public async Task<int> ImportAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        using var db = await this.connectionFactory.CreateAsync(connectionId, ct).ConfigureAwait(false);
        var isSqlite = db.DataProvider.Name.Contains("SQLite", StringComparison.OrdinalIgnoreCase);

        var tables = isSqlite ? ReadSqlite(db) : ReadPostgres(db);

        foreach (var table in tables)
        {
            await this.catalog.UpsertAsync(
                new WorkflowTableMetadata(connectionId, table.TableName, table.Schema, table.Columns),
                ct).ConfigureAwait(false);
        }

        return tables.Count;
    }

    // ── SQLite ───────────────────────────────────────────────────────────────────────────

    private static List<TableSchema> ReadSqlite(DataConnection db)
    {
        var tableNames = new List<string>();
        foreach (var row in Read(db, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name"))
        {
            tableNames.Add(Convert.ToString(row["name"]) ?? string.Empty);
        }

        var result = new List<TableSchema>();
        foreach (var name in tableNames)
        {
            var columns = new List<WorkflowColumnMetadata>();
            foreach (var col in Read(db, $"PRAGMA table_info(\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\")"))
            {
                var colName = Convert.ToString(col["name"]) ?? string.Empty;
                var dataType = Convert.ToString(col["type"]) ?? string.Empty;
                var notNull = Convert.ToInt64(col["notnull"] ?? 0L) != 0;
                columns.Add(new WorkflowColumnMetadata(colName, dataType, !notNull));
            }

            result.Add(new TableSchema(name, null, columns));
        }

        return result;
    }

    // ── Postgres ─────────────────────────────────────────────────────────────────────────

    private static List<TableSchema> ReadPostgres(DataConnection db)
    {
        var pairs = new List<(string Schema, string Table)>();
        const string tablesSql =
            "SELECT table_schema, table_name FROM information_schema.tables "
            + "WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog','information_schema') "
            + "ORDER BY table_schema, table_name";
        foreach (var row in Read(db, tablesSql))
        {
            pairs.Add((Convert.ToString(row["table_schema"]) ?? "public", Convert.ToString(row["table_name"]) ?? string.Empty));
        }

        var result = new List<TableSchema>();
        foreach (var (schema, table) in pairs)
        {
            var columns = new List<WorkflowColumnMetadata>();
            const string colsSql =
                "SELECT column_name, data_type, is_nullable FROM information_schema.columns "
                + "WHERE table_schema = @s AND table_name = @t ORDER BY ordinal_position";
            foreach (var col in Read(db, colsSql, new DataParameter("s", schema), new DataParameter("t", table)))
            {
                var colName = Convert.ToString(col["column_name"]) ?? string.Empty;
                var dataType = Convert.ToString(col["data_type"]) ?? string.Empty;
                var nullable = string.Equals(Convert.ToString(col["is_nullable"]), "YES", StringComparison.OrdinalIgnoreCase);
                columns.Add(new WorkflowColumnMetadata(colName, dataType, nullable));
            }

            result.Add(new TableSchema(table, schema, columns));
        }

        return result;
    }

    // ── Reader helper ──────────────────────────────────────────────────────────────────────

    private static IEnumerable<IReadOnlyDictionary<string, object?>> Read(DataConnection db, string sql, params DataParameter[] parameters)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var reader = db.ExecuteReader(sql, parameters);
        IDataReader r = reader.Reader!;
        var fieldCount = r.FieldCount;
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(fieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < fieldCount; i++)
            {
                var value = r.GetValue(i);
                row[r.GetName(i)] = value is DBNull ? null : value;
            }

            rows.Add(row);
        }

        return rows;
    }

    private sealed record TableSchema(string TableName, string? Schema, IReadOnlyList<WorkflowColumnMetadata> Columns);
}

