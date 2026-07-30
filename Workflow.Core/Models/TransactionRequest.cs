// <copyright file="TransactionRequest.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Core.Models;

/// <summary>
/// 💼 Request returned by <c>builtin.database.transaction</c> so the engine can open a database
/// transaction and orchestrate the connected body sub-graph inside it~ ✨💖
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Mirrors <see cref="TryCatchRequest"/> structurally. <c>WorkflowExecutor</c>
/// detects a non-null <c>ModuleResult.Transaction</c> after <c>NodeExecutionCompleted</c>
/// and spawns a <c>TransactionExecutorActor</c> to run the body~ 🌸.
/// </para>
/// <para>
/// Port names default to their canonical values (<c>"transactionBody"</c>,
/// <c>"committed"</c>, <c>"rolledBack"</c>) but are configurable for advanced patterns~
/// </para>
/// </remarks>
public sealed class TransactionRequest
{
    /// <summary>
    /// Gets or sets the output port name whose connections form the transaction body sub-graph~
    /// Default: <c>"transactionBody"</c>~ 💼
    /// </summary>
    public string BodyPort { get; init; } = "transactionBody";

    /// <summary>
    /// Gets or sets the output port name fired after the body commits successfully~
    /// Default: <c>"committed"</c>~ ✅
    /// </summary>
    public string CommittedPort { get; init; } = "committed";

    /// <summary>
    /// Gets or sets the output port name fired after the body rolls back due to failure~
    /// Default: <c>"rolledBack"</c>~ ↩️
    /// </summary>
    public string RolledBackPort { get; init; } = "rolledBack";

    /// <summary>
    /// Gets or sets the named database connection id used for auto-enlistment~ 📇
    /// </summary>
    public string? ConnectionId { get; init; }

    /// <summary>
    /// Gets or sets the raw connection string used when no named connection id is supplied~ 🔓
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Gets or sets the provider key used with <see cref="ConnectionString"/>~ 🗂️
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets or sets the requested isolation level. <see langword="null"/> means ReadCommitted~ 🔒
    /// </summary>
    public string? IsolationLevel { get; init; }

    /// <summary>
    /// Gets or sets the command timeout, in seconds, applied to the transaction connection~ ⏱️
    /// </summary>
    public int? TimeoutSeconds { get; init; }
}
