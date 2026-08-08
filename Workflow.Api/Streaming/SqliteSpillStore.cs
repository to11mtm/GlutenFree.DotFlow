// <copyright file="SqliteSpillStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Workflow.Modules.Streaming;

/// <summary>
/// 🗄️ Phase 5.1.6 — the recommended <b>local</b> spill store: one SQLite file.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: One provisionable file beats thousands of scratch files — an operator can put it on
/// a chosen volume, size it, and watch it. It does <b>not</b> escape a slow local device, though:
/// SQLite still consumes local IOPS. On hosts with throttled local disk use the blob spill store
/// instead (plan §13, Q-C1)~ 🎯.
/// </para>
/// <para>
/// Durability is deliberately switched <b>off</b> (<c>journal_mode=MEMORY</c>,
/// <c>synchronous=OFF</c>). Spill is scratch: if the process dies, the execution died with it and
/// the data is garbage. Paying fsync costs to protect bytes nobody will ever read again would be
/// the worst of both worlds~ 🏎️.
/// </para>
/// </remarks>
public sealed class SqliteSpillStore : ISpillStore, IAsyncDisposable
{
    private readonly string connectionString;
    private readonly SqliteConnection keepAlive;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteSpillStore"/> class~ 🗄️.
    /// </summary>
    /// <param name="path">The spill database file path.</param>
    public SqliteSpillStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        // CopilotNote: an in-memory data source only lives as long as some connection holds it
        // open. Keeping one connection for the store's lifetime makes ":memory:" spill usable in
        // tests without changing any of the production paths~ 🧪
        this.keepAlive = new SqliteConnection(this.connectionString);
        this.keepAlive.Open();

        using var pragma = this.keepAlive.CreateCommand();
        pragma.CommandText =
            """
            PRAGMA journal_mode = MEMORY;
            PRAGMA synchronous  = OFF;
            PRAGMA temp_store   = MEMORY;
            CREATE TABLE IF NOT EXISTS spill_segments (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                execution_id  TEXT    NOT NULL,
                region_index  INTEGER NOT NULL,
                node_id       TEXT    NOT NULL,
                seq           INTEGER NOT NULL,
                payload       BLOB    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_spill_session
                ON spill_segments (execution_id, region_index, node_id, seq);
            """;
        pragma.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public string Name => "sqlite";

    /// <inheritdoc/>
    public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default)
        => Task.FromResult<ISpillSession>(new Session(this.connectionString, key));

    /// <inheritdoc/>
    public async Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(this.connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM spill_segments WHERE execution_id = $e;";
        cmd.Parameters.AddWithValue("$e", executionId.ToString("N", CultureInfo.InvariantCulture));
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(this.connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT execution_id FROM spill_segments;";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (Guid.TryParseExact(reader.GetString(0), "N", out var id))
            {
                yield return id;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await this.keepAlive.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class Session : ISpillSession
    {
        private readonly string connectionString;
        private readonly SpillSessionKey key;
        private readonly string executionId;
        private SqliteConnection? connection;
        private long sequence;
        private bool disposed;

        public Session(string connectionString, SpillSessionKey key)
        {
            this.connectionString = connectionString;
            this.key = key;
            this.executionId = key.ExecutionId.ToString("N", CultureInfo.InvariantCulture);
        }

        public long Count { get; private set; }

        public long BytesWritten { get; private set; }

        public async Task AppendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            var conn = await this.EnsureConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO spill_segments (execution_id, region_index, node_id, seq, payload) " +
                "VALUES ($e, $r, $n, $s, $p);";
            cmd.Parameters.AddWithValue("$e", this.executionId);
            cmd.Parameters.AddWithValue("$r", this.key.RegionIndex);
            cmd.Parameters.AddWithValue("$n", this.key.NodeId);
            cmd.Parameters.AddWithValue("$s", this.sequence++);
            cmd.Parameters.AddWithValue("$p", payload.ToArray());

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            this.Count++;
            this.BytesWritten += payload.Length;
        }

        // CopilotNote: every append is its own committed statement, so there is nothing pending~
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async IAsyncEnumerable<byte[]> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            var conn = await this.EnsureConnectionAsync(ct).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT payload FROM spill_segments " +
                "WHERE execution_id = $e AND region_index = $r AND node_id = $n " +
                "ORDER BY seq;";
            cmd.Parameters.AddWithValue("$e", this.executionId);
            cmd.Parameters.AddWithValue("$r", this.key.RegionIndex);
            cmd.Parameters.AddWithValue("$n", this.key.NodeId);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                yield return (byte[])reader.GetValue(0);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            if (this.connection is null)
            {
                return;
            }

            try
            {
                await using var cmd = this.connection.CreateCommand();
                cmd.CommandText =
                    "DELETE FROM spill_segments " +
                    "WHERE execution_id = $e AND region_index = $r AND node_id = $n;";
                cmd.Parameters.AddWithValue("$e", this.executionId);
                cmd.Parameters.AddWithValue("$r", this.key.RegionIndex);
                cmd.Parameters.AddWithValue("$n", this.key.NodeId);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (SqliteException)
            {
                // CopilotNote: cleanup is best-effort — the sweeper reclaims what dispose misses,
                // and throwing here would mask whatever real failure unwound the stage~ 🧹
            }
            finally
            {
                await this.connection.DisposeAsync().ConfigureAwait(false);
                this.connection = null;
            }
        }

        private async Task<SqliteConnection> EnsureConnectionAsync(CancellationToken ct)
        {
            if (this.connection is not null)
            {
                return this.connection;
            }

            var conn = new SqliteConnection(this.connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            this.connection = conn;
            return conn;
        }
    }
}
