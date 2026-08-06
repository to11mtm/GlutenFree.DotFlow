// <copyright file="TestSqlite.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests;

using System;

/// <summary>
/// 🧪 In-memory SQLite connection strings that actually behave like a database~ 💾
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Tests used to configure <c>Persistence:ConnectionString</c> as a bare
/// <c>:memory:</c>. With Microsoft.Data.Sqlite that gives <b>every connection its own private,
/// empty database</b> — so the migration runner creates the schema on one connection, that
/// database is discarded when the connection closes, and every later operation opens a fresh empty
/// one. A probe reproduced it exactly: create a table on connection A, read it from connection B →
/// <c>SQLite Error 1: 'no such table'</c>.
/// </para>
/// <para>
/// The result wasn't a clean failure but an <b>intermittent</b> one — whether a given operation saw
/// a schema depended on connection pooling and timing, which is why these tests passed alone and
/// failed under parallel load. Naming the database and adding <c>Cache=Shared</c> makes every
/// connection with the same string share one database, while a unique name per fixture keeps test
/// classes isolated from each other~ 🛡️.
/// </para>
/// </remarks>
public static class TestSqlite
{
    /// <summary>
    /// Creates a connection string for a <b>private, shared-cache</b> in-memory database. 💾.
    /// </summary>
    /// <param name="label">
    /// Optional label woven into the database name — handy when reading logs; uniqueness comes from
    /// a GUID either way.
    /// </param>
    /// <returns>A connection string safe to share across connections but isolated per caller.</returns>
    public static string InMemory(string? label = null)
        => $"Data Source=file:{Sanitize(label)}{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    private static string Sanitize(string? label)
        => string.IsNullOrWhiteSpace(label)
            ? "dotflow-test-"
            : new string(Array.FindAll(label.ToCharArray(), char.IsLetterOrDigit)) + "-";
}
