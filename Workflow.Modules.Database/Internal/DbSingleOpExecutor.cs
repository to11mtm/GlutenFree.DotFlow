// <copyright file="DbSingleOpExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Internal;

using System;
using System.Globalization;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;

/// <summary>
/// 🧷 Runs a single parameterised statement and resolves an optional provider-aware
/// last-insert id — shared by <c>DatabaseExecuteModule</c> (2.4.a.2) and
/// <c>DatabaseTransactionModule</c> (2.4.a.3)~ ✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.3 folded this out of the execute module so both modules resolve
/// <c>lastInsertId</c> identically. SQLite → <c>last_insert_rowid()</c> on the same open
/// connection; Postgres → the first column of a user-supplied <c>RETURNING</c> clause (Q12 —
/// no auto-rewrite). This is also the seam a future true-prepared batch path can plug into~ 🌸.
/// </remarks>
public static class DbSingleOpExecutor
{
    /// <summary>
    /// Executes <paramref name="sql"/> with <paramref name="parameters"/> and returns the
    /// affected-row count plus an optional last-insert id~ 🆔.
    /// </summary>
    /// <param name="db">The open connection (inside a transaction when called by the transaction module).</param>
    /// <param name="sql">The verbatim SQL.</param>
    /// <param name="parameters">Bound parameters (may be empty).</param>
    /// <param name="expectLastInsertId">Whether to resolve a last-insert id.</param>
    /// <param name="logger">Logger for provider-specific warnings.</param>
    /// <returns>The affected row count and the resolved last-insert id (or null).</returns>
    public static (int AffectedRows, long? LastInsertId) Execute(
        DataConnection db,
        string sql,
        DataParameter[] parameters,
        bool expectLastInsertId,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);

        var isPostgres = db.DataProvider.Name.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase);

        // 🐘 Postgres: the id (if any) rides on a user-supplied RETURNING clause (Q12 — no auto-rewrite).
        // Read it via a reader so both the returned value AND the row count are captured~
        if (expectLastInsertId && isPostgres)
        {
            long? returningId = null;
            var rowCount = 0;
            using var reader = db.ExecuteReader(sql, parameters);
            var r = reader.Reader!;
            while (r.Read())
            {
                rowCount++;
                if (returningId is null && r.FieldCount > 0)
                {
                    var value = r.GetValue(0);
                    if (value is not DBNull)
                    {
                        returningId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    }
                }
            }

            if (returningId is null)
            {
                logger.LogWarning(
                    "⚠️ expectLastInsertId was set but the Postgres statement returned no RETURNING value — lastInsertId is null. Add 'RETURNING id' to your INSERT~ 🌸");
            }

            return (rowCount, returningId);
        }

        var affected = db.Execute(sql, parameters);

        if (!expectLastInsertId)
        {
            return (affected, null);
        }

        // 🪶 SQLite: last_insert_rowid() on the SAME open connection~
        long? lastId = db.Execute<long?>("SELECT last_insert_rowid()");
        return (affected, lastId);
    }
}
