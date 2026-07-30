// <copyright file="IAmbientDbTransactions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Abstractions;

using System;

/// <summary>
/// 💼 Registry for database connections owned by an engine-managed structural transaction~ ✨
/// </summary>
/// <remarks>
/// The value is intentionally <see cref="object"/> so Workflow.Engine stays decoupled from
/// linq2db and the database module assembly. Database modules cast the value they registered~ 🌸.
/// </remarks>
public interface IAmbientDbTransactions
{
    /// <summary>Tries to get an ambient transaction connection for an execution + connection id~ 🔎.</summary>
    public object? TryGet(Guid executionId, string connectionId);

    /// <summary>Registers an ambient transaction connection for an execution + connection id~ 📌.</summary>
    public void Register(Guid executionId, string connectionId, object connection);

    /// <summary>Removes an ambient transaction connection for an execution + connection id~ 🧹.</summary>
    public void Remove(Guid executionId, string connectionId);
}
