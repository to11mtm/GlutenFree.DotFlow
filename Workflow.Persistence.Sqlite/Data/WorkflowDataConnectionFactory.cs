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



