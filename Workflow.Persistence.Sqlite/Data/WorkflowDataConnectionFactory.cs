// <copyright file="WorkflowDataConnectionFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data;

using LinqToDB;
using LinqToDB.Data;

/// <summary>
/// 🏭 Creates <see cref="WorkflowDataConnection"/> instances from a connection string~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: Call <see cref="EnsureProviderRegistered"/> once at startup before creating
/// any connections — it registers the SQLite linq2db provider with the runtime~ 🪶
/// </remarks>
public sealed class WorkflowDataConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowDataConnectionFactory"/> class~ 🔌.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public WorkflowDataConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>
    /// Gets a value indicating whether the database lives in memory (<c>:memory:</c>,
    /// <c>Mode=Memory</c>, or a <c>file:…?mode=memory</c> URI) — gone when the process ends~ 🧠.
    /// </summary>
    public bool IsInMemory
    {
        get
        {
            if (string.Equals(_connectionString.Trim(), ":memory:", StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(_connectionString);
                return builder.Mode == Microsoft.Data.Sqlite.SqliteOpenMode.Memory
                       || string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal)
                       || builder.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Ensures the SQLite linq2db provider is registered with the runtime.
    /// Call this once at application startup~ ✅.
    /// </summary>
    public static void EnsureProviderRegistered()
    {
        // CopilotNote: linq2db 6.x no longer needs ResolveSQLite() — the provider registers
        // automatically when UseSQLite() is called on DataOptions. This method is kept as a
        // no-op so callers don't need to change~ 🪶
    }

    /// <summary>Creates a new <see cref="WorkflowDataConnection"/>~ 🗄️.</summary>
    public WorkflowDataConnection Create()
    {
        var options = new DataOptions()
            .UseSQLite(_connectionString);

        return new WorkflowDataConnection(options);
    }
}



