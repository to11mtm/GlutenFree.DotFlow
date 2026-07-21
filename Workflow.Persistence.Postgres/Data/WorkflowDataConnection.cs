// <copyright file="WorkflowDataConnection.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Postgres.Data;

using LinqToDB;
using LinqToDB.Data;
using Workflow.Persistence.Postgres.Data.Entities;

/// <summary>
/// 🗄️ Linq2Db DataConnection for PostgreSQL with typed table accessors~ ✨💖
/// </summary>
public class WorkflowDataConnection : DataConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowDataConnection"/> class~ 🔌.
    /// </summary>
    /// <param name="options">The Linq2Db data connection options.</param>
    public WorkflowDataConnection(DataOptions options)
        : base(options)
    {
    }

    /// <summary>Gets the <c>workflows</c> table~ 📋.</summary>
    public ITable<WorkflowEntity> Workflows => this.GetTable<WorkflowEntity>();

    /// <summary>Gets the <c>executions</c> table~ 📊.</summary>
    public ITable<ExecutionEntity> Executions => this.GetTable<ExecutionEntity>();

    /// <summary>Gets the <c>execution_nodes</c> table~ 🌸.</summary>
    public ITable<ExecutionNodeEntity> ExecutionNodes => this.GetTable<ExecutionNodeEntity>();

    /// <summary>Gets the <c>variables</c> table~ 💾.</summary>
    public ITable<VariableEntity> Variables => this.GetTable<VariableEntity>();
}

