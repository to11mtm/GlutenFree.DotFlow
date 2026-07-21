// <copyright file="DatabaseConnectionsOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Configuration;

using System.Collections.Generic;
using Workflow.Modules.Database.Abstractions;

/// <summary>
/// ⚙️ Options bound from <c>appsettings.json: Workflow:Database</c>~ ✨
/// Seeds the connection registry at startup and controls runtime-CRUD availability (D4).
/// </summary>
/// <remarks>
/// <para>
/// Example config:
/// <code>
/// "Workflow": {
///   "Database": {
///     "DisableRuntimeCrud": false,
///     "Connections": {
///       "OrdersDb": {
///         "Id": "OrdersDb",
///         "ProviderKey": "postgres",
///         "ConnectionString": "Host=localhost;Database=orders;…"
///       }
///     }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// CopilotNote: Landed early in 2.4.a.0 (planned for 2.4.a.5) because
/// <c>InMemoryDbConnectionRegistry</c> hydrates from it at construction time.
/// The API-surface consumers (controller + persistence override) still arrive in 2.4.a.5~ 🌸.
/// </para>
/// </remarks>
public sealed class DatabaseConnectionsOptions
{
    /// <summary>
    /// The configuration section path this binds from. 🧭.
    /// </summary>
    public const string SectionName = "Workflow:Database";

    /// <summary>
    /// Gets or sets the named connections keyed by id (the key wins over the descriptor's Id when they differ). 📇.
    /// </summary>
    public Dictionary<string, DbConnectionDescriptor> Connections { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether runtime CRUD of connections via the API is disabled.
    /// Default <c>false</c> — runtime CRUD is opt-out per D4~ 🔐.
    /// </summary>
    public bool DisableRuntimeCrud { get; set; }
}

