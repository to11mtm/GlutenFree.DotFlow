// <copyright file="WorkflowDataConnectionFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using LinqToDB;

namespace Workflow.Persistence.Postgres.Data;

using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.PostgreSQL;

/// <summary>
/// 🏭 Creates <see cref="WorkflowDataConnection"/> instances for PostgreSQL~ ✨💖
/// </summary>
public sealed class WorkflowDataConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowDataConnectionFactory"/> class~ 🔌.
    /// </summary>
    /// <param name="connectionString">The Npgsql connection string.</param>
    public WorkflowDataConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>Creates a new <see cref="WorkflowDataConnection"/>~ 🗄️.</summary>
    public WorkflowDataConnection Create()
    {
        var options = new DataOptions()
            .UsePostgreSQL(_connectionString);

        return new WorkflowDataConnection(options);
    }
}


