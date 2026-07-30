// <copyright file="SqliteDbConnectionRegistry.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Connections;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;

/// <summary>
/// 📇💾 SQLite-persisted connection registry — connections defined at runtime (e.g. from Linq
/// Studio) survive restarts~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Two origins live in one table: <c>config</c> rows are re-seeded from
/// <see cref="DatabaseConnectionsOptions.Connections"/> on every startup (ops' appsettings stay
/// authoritative), while <c>user</c> rows come from the API/UI and are only ever changed by the
/// API/UI. Lookup is case-insensitive.
/// </para>
/// <para>
/// Connection strings pass through <see cref="IConnectionStringProtector"/> on the way in and out,
/// so at-rest encryption is a host-swappable seam. The library default is pass-through — fine for
/// dev/MVP, not for production secrets~ 🔐.
/// </para>
/// </remarks>
public sealed class SqliteDbConnectionRegistry : IDbConnectionRegistry
{
    private const string ConfigOrigin = "config";
    private const string UserOrigin = "user";

    private readonly string connectionString;
    private readonly IConnectionStringProtector protector;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="SqliteDbConnectionRegistry"/> class~ 💾.</summary>
    /// <param name="storeOptions">The store options (file path).</param>
    /// <param name="configOptions">The config-bound connections seeded on startup.</param>
    /// <param name="protector">The at-rest protector for connection strings.</param>
    public SqliteDbConnectionRegistry(
        IOptions<ConnectionStoreOptions> storeOptions,
        IOptions<DatabaseConnectionsOptions> configOptions,
        IConnectionStringProtector protector)
    {
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(configOptions);

        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));

        var path = Path.GetFullPath(storeOptions.Value.Path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 15,
        }.ToString();

        this.EnsureSchema();
        this.SeedFromConfig(configOptions.Value);
    }

    /// <inheritdoc/>
    public async Task<Option<DbConnectionDescriptor>> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = this.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, provider_key, connection_string, display_name, enabled FROM connections WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);

            using var reader = cmd.ExecuteReader();
            return reader.Read()
                ? Option<DbConnectionDescriptor>.Some(this.ReadDescriptor(reader))
                : Option<DbConnectionDescriptor>.None;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DbConnectionDescriptor>> ListAsync(CancellationToken ct = default)
    {
        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = this.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT id, provider_key, connection_string, display_name, enabled FROM connections ORDER BY id";

            var result = new List<DbConnectionDescriptor>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(this.ReadDescriptor(reader));
            }

            return result;
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(DbConnectionDescriptor descriptor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = this.Open();
            this.Write(conn, descriptor, UserOrigin);
        }
        finally
        {
            this.gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await this.gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = this.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM connections WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(this.connectionString);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = this.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS connections (
                id                TEXT PRIMARY KEY COLLATE NOCASE,
                provider_key      TEXT NOT NULL,
                connection_string TEXT NOT NULL,
                display_name      TEXT NULL,
                enabled           INTEGER NOT NULL DEFAULT 1,
                origin            TEXT NOT NULL DEFAULT 'user',
                updated_at        TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private void SeedFromConfig(DatabaseConnectionsOptions options)
    {
        if (options.Connections.Count == 0)
        {
            return;
        }

        using var conn = this.Open();
        using var tx = conn.BeginTransaction();

        // Config is authoritative for its own rows — drop stale ones, then re-write~ ⚙️
        using (var clear = conn.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM connections WHERE origin = $origin";
            clear.Parameters.AddWithValue("$origin", ConfigOrigin);
            clear.ExecuteNonQuery();
        }

        foreach (var (key, descriptor) in options.Connections)
        {
            this.Write(conn, descriptor with { Id = key }, ConfigOrigin, tx);
        }

        tx.Commit();
    }

    private void Write(SqliteConnection conn, DbConnectionDescriptor descriptor, string origin, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO connections (id, provider_key, connection_string, display_name, enabled, origin, updated_at)
            VALUES ($id, $provider, $cs, $display, $enabled, $origin, $updated)
            ON CONFLICT(id) DO UPDATE SET
                provider_key      = excluded.provider_key,
                connection_string = excluded.connection_string,
                display_name      = excluded.display_name,
                enabled           = excluded.enabled,
                origin            = excluded.origin,
                updated_at        = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$id", descriptor.Id);
        cmd.Parameters.AddWithValue("$provider", descriptor.ProviderKey);
        cmd.Parameters.AddWithValue("$cs", this.protector.Protect(descriptor.ConnectionString ?? string.Empty));
        cmd.Parameters.AddWithValue("$display", (object?)descriptor.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$enabled", descriptor.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$origin", origin);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private DbConnectionDescriptor ReadDescriptor(SqliteDataReader reader)
        => new(
            Id: reader.GetString(0),
            ProviderKey: reader.GetString(1),
            ConnectionString: this.SafeUnprotect(reader.GetString(2), reader.GetString(0), out var usable),
            DisplayName: reader.IsDBNull(3) ? null : reader.GetString(3),
            Enabled: reader.GetInt64(4) != 0 && usable);

    /// <summary>
    /// Unprotects defensively: a row written under different protection keys (or by a different
    /// protector) must not take down the whole listing — it surfaces as a disabled connection~ 🛟.
    /// </summary>
    private string SafeUnprotect(string stored, string id, out bool usable)
    {
        try
        {
            usable = true;
            return this.protector.Unprotect(stored);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            usable = false;
            return string.Empty;
        }
    }
}
