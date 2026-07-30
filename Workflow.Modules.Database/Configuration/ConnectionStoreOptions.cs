// <copyright file="ConnectionStoreOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Configuration;

/// <summary>
/// ⚙️ Options for the persisted connection store (<c>Workflow:Database:ConnectionStore</c>)~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Enabled"/> is true the named-connection registry is backed by a small,
/// purpose-built SQLite file instead of process memory, so connections defined through the UI
/// survive restarts. Connection strings are written through <c>IConnectionStringProtector</c> —
/// the seam a host uses to supply real at-rest encryption (the API registers a Data-Protection
/// backed protector; the library default is pass-through)~ 🔐.
/// </para>
/// </remarks>
public sealed class ConnectionStoreOptions
{
    /// <summary>The configuration section path this binds from. 🧭.</summary>
    public const string SectionName = "Workflow:Database:ConnectionStore";

    /// <summary>
    /// Gets or sets a value indicating whether connections are persisted to SQLite.
    /// Defaults to <c>false</c> so libraries/tests keep the in-memory registry; hosts opt in~ 💾.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the SQLite file path (relative paths resolve against the process dir)~ 📁.</summary>
    public string Path { get; set; } = "dotflow-connections.db";
}
