// <copyright file="SpillAgeRetentionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Streaming;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Api.Streaming;
using Workflow.Core.Models;
using Workflow.Modules.Streaming;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// §9.6 (B2) — with no execution history to key retention to, the spill sweeper falls back to the
/// spill's own age: spill nothing has written to for longer than the abandoned threshold is
/// reclaimed, unknown age is kept, and the host warns at startup.
/// </summary>
public sealed class SpillAgeRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static SpillCleanupHostedService Service(ISpillStore store, IExecutionHistoryRepository? history = null, ILogger<SpillCleanupHostedService>? logger = null)
        => new(
            store,
            Options.Create(new SpillOptions
            {
                RetentionAfterCompletion = TimeSpan.FromHours(1),
                AbandonedThreshold = TimeSpan.FromDays(2),
                SweepInterval = TimeSpan.FromHours(1),
            }),
            logger ?? NullLogger<SpillCleanupHostedService>.Instance,
            history,
            new FixedClock(Now));

    [Fact]
    public async Task NoHistory_ReclaimsSpillUntouchedForLongerThanTheAbandonedThreshold()
    {
        var old = Guid.NewGuid();
        var store = new AgedSpillStore { [old] = Now.AddDays(-3) };

        (await Service(store).SweepAsync()).Should().Be(1);

        store.Purged.Should().Equal(old);
    }

    [Fact]
    public async Task NoHistory_KeepsSpillWrittenWithinTheThreshold()
    {
        var recent = Guid.NewGuid();
        var store = new AgedSpillStore { [recent] = Now.AddDays(-1) };

        (await Service(store).SweepAsync()).Should().Be(0);

        store.Purged.Should().BeEmpty(because: "a run may still be using it");
    }

    [Fact]
    public async Task NoHistory_KeepsSpillOfUnknownAge()
    {
        var unknown = Guid.NewGuid();
        var store = new AgelessSpillStore(unknown);

        (await Service(store).SweepAsync()).Should().Be(0);

        store.Purged.Should().BeEmpty(because: "a store that can't tell the age gets the old, safe behaviour");
    }

    [Fact]
    public async Task WithHistory_TheRunsStateDecides_NotTheSpillsAge()
    {
        var running = Guid.NewGuid();
        var store = new AgedSpillStore { [running] = Now.AddDays(-10) };
        var history = new OneRecordHistory(new ExecutionRecord(running, Guid.NewGuid(), ExecutionState.Running, Now.AddHours(-1)));

        (await Service(store, history).SweepAsync()).Should().Be(0);

        store.Purged.Should().BeEmpty(because: "with history, a run that started an hour ago is still running");
    }

    [Fact]
    public async Task NoHistory_WarnsAtStartup()
    {
        var logger = new CapturingLogger();
        var service = Service(new AgedSpillStore(), logger: logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        logger.Warnings.Should().ContainSingle(m => m.Contains("there is no execution history store") && m.Contains("reclaimed by age"));
    }

    [Fact]
    public async Task WithHistory_DoesNotWarn()
    {
        var logger = new CapturingLogger();
        var service = Service(new AgedSpillStore(), new OneRecordHistory(null), logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        logger.Warnings.Should().BeEmpty();
    }

    // ── Every store reports when an execution's spill was last written ──────────────────────

    [Fact]
    public async Task SqliteStore_ReportsTheLastWrite()
    {
        await using var store = new SqliteSpillStore($"file:spill-age-{Guid.NewGuid():N}?mode=memory");
        await AssertReportsLastWrite(store);
    }

    [Fact]
    public async Task DirectoryStore_ReportsTheLastWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotflow-spill-age-" + Guid.NewGuid().ToString("N"));
        try
        {
            await AssertReportsLastWrite(new DirectorySpillStore(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BlobStore_ReportsTheNewestSegment()
    {
        var blobs = new TimedBlobStore();
        var store = new BlobStoreSpillStore(blobs, segmentRecords: 1);
        var key = new SpillSessionKey(Guid.NewGuid(), 0, "collect");

        (await store.GetLastWrittenAsync(key.ExecutionId)).Should().BeNull();

        await using (var session = await store.BeginAsync(key))
        {
            blobs.Clock = Now.AddMinutes(-10);
            await session.AppendAsync(new byte[] { 1 });
            await session.FlushAsync();
            blobs.Clock = Now.AddMinutes(-2);
            await session.AppendAsync(new byte[] { 2 });
            await session.FlushAsync();

            (await store.GetLastWrittenAsync(key.ExecutionId)).Should().Be(Now.AddMinutes(-2));
        }
    }

    [Fact]
    public async Task SqliteStore_UpgradesAnOlderScratchFile_AndTreatsItsOldRowsAsUnknownAge()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotflow-spill-legacy-{Guid.NewGuid():N}.db");
        var legacy = Guid.NewGuid();
        try
        {
            await using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE spill_segments (id INTEGER PRIMARY KEY AUTOINCREMENT, execution_id TEXT NOT NULL, " +
                    "region_index INTEGER NOT NULL, node_id TEXT NOT NULL, seq INTEGER NOT NULL, payload BLOB NOT NULL);" +
                    $"INSERT INTO spill_segments (execution_id, region_index, node_id, seq, payload) VALUES ('{legacy:N}', 0, 'n', 0, x'01');";
                await cmd.ExecuteNonQueryAsync();
            }

            await using var store = new SqliteSpillStore(path);

            (await store.GetLastWrittenAsync(legacy)).Should().BeNull(because: "rows from before the column existed have no time");
            await AssertReportsLastWrite(store);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task AssertReportsLastWrite(ISpillStore store)
    {
        var key = new SpillSessionKey(Guid.NewGuid(), 0, "collect");
        (await store.GetLastWrittenAsync(key.ExecutionId)).Should().BeNull();

        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        await using var session = await store.BeginAsync(key);
        await session.AppendAsync(new byte[] { 1, 2, 3 });
        await session.FlushAsync();
        var after = DateTimeOffset.UtcNow.AddSeconds(2);

        (await store.GetLastWrittenAsync(key.ExecutionId)).Should().NotBeNull()
            .And.Subject.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────────────────────

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now) => this.now = now;

        public override DateTimeOffset GetUtcNow() => this.now;
    }

    /// <summary>A spill store whose executions have a known last-write time.</summary>
    private sealed class AgedSpillStore : Dictionary<Guid, DateTimeOffset>, ISpillStore
    {
        public List<Guid> Purged { get; } = new();

        public string Name => "aged";

        public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
        {
            this.Purged.Add(executionId);
            this.Remove(executionId);
            return Task.FromResult(1);
        }

        public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var id in this.Keys.ToArray())
            {
                yield return id;
            }

            await Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetLastWrittenAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<DateTimeOffset?>(this.TryGetValue(executionId, out var at) ? at : null);
    }

    /// <summary>A spill store that doesn't implement the age method (uses the interface default).</summary>
    private sealed class AgelessSpillStore : ISpillStore
    {
        private readonly List<Guid> executions;

        public AgelessSpillStore(params Guid[] executions) => this.executions = executions.ToList();

        public List<Guid> Purged { get; } = new();

        public string Name => "ageless";

        public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
        {
            this.Purged.Add(executionId);
            return Task.FromResult(1);
        }

        public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var id in this.executions)
            {
                yield return id;
            }

            await Task.CompletedTask;
        }
    }

    /// <summary>A history holding at most one execution record.</summary>
    private sealed class OneRecordHistory : IExecutionHistoryRepository
    {
        private readonly ExecutionRecord? record;

        public OneRecordHistory(ExecutionRecord? record) => this.record = record;

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult(this.record?.ExecutionId == executionId ? this.record : null);

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default) => throw new NotSupportedException();

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<SpillCleanupHostedService>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IEnumerable<string> Warnings => this.Messages.Where(m => m.StartsWith("Warning:", StringComparison.Ordinal));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => this.Messages.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }

    /// <summary>A blob store whose LastModified comes from a settable clock.</summary>
    private sealed class TimedBlobStore : IBlobStore
    {
        private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset At)> store = new(StringComparer.Ordinal);

        public DateTimeOffset Clock { get; set; } = Now;

        public async Task<string> PutAsync(string key, Stream data, string? contentType = null, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await data.CopyToAsync(ms, ct);
            this.store[key] = (ms.ToArray(), this.Clock);
            return "etag";
        }

        public Task<Stream?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult<Stream?>(this.store.TryGetValue(key, out var e) ? new MemoryStream(e.Bytes, writable: false) : null);

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default) => Task.FromResult(this.store.Remove(key));

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(this.store.ContainsKey(key));

        public async IAsyncEnumerable<BlobInfo> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var (key, e) in this.store.ToList())
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    yield return new BlobInfo(key, e.Bytes.Length, e.At);
                }
            }

            await Task.CompletedTask;
        }

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
