// <copyright file="WorkflowDataConnection.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Sqlite.Data;

using LinqToDB;
using LinqToDB.Data;
using Workflow.Persistence.Sqlite.Data.Entities;

/// <summary>
/// ️ Linq2Db DataConnection that exposes typed table accessors for all workflow tables~ ✨
/// </summary>
public class WorkflowDataConnection : DataConnection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WorkflowDataConnection"/> class~ .
    /// </summary>
    /// <param name="options">The Linq2Db data connection options.</param>
    public WorkflowDataConnection(DataOptions options)
        : base(options)
    {
    }

    /// <summary>Gets the <c>workflows</c> table~ .</summary>
    public ITable<WorkflowEntity> Workflows => this.GetTable<WorkflowEntity>();

    /// <summary>Gets the <c>executions</c> table~ .</summary>
    public ITable<ExecutionEntity> Executions => this.GetTable<ExecutionEntity>();

    /// <summary>Gets the <c>execution_nodes</c> table~ .</summary>
    public ITable<ExecutionNodeEntity> ExecutionNodes => this.GetTable<ExecutionNodeEntity>();

    /// <summary>Gets the <c>variables</c> table~ .</summary>
    public ITable<VariableEntity> Variables => this.GetTable<VariableEntity>();

    /// <summary>Gets the <c>blob_store</c> table~ ️.</summary>
    public ITable<BlobEntity> Blobs => this.GetTable<BlobEntity>();

    /// <summary>Gets the <c>webhook_registrations</c> table~ .</summary>
    public ITable<WebhookRegistrationEntity> WebhookRegistrations => this.GetTable<WebhookRegistrationEntity>();

    /// <summary>Gets the <c>db_connections</c> table (Phase 2.4.a.5)~ 📇.</summary>
    public ITable<DbConnectionEntity> DbConnections => this.GetTable<DbConnectionEntity>();
}

