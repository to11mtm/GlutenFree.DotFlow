// <copyright file="DirectorySpillStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Streaming;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Workflow.Modules.Streaming;

/// <summary>
/// 📁 Phase 5.1.6 — spills to plain files under a configured directory. <b>Explicit opt-in only.</b>
/// </summary>
/// <remarks>
/// CopilotNote: Offered because sometimes a fast local scratch volume really is the right answer
/// and a file is the simplest thing that works. It is <b>not</b> a default and never becomes one:
/// choosing it means asserting you know the local disk is fast. If you're unsure, the SQLite store
/// gives you one provisionable file instead of a tree, and the blob store gets scratch off the node
/// entirely (plan §11, §13)~ 🎯
/// </remarks>
public sealed class DirectorySpillStore : ISpillStore
{
    private readonly string root;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectorySpillStore"/> class~ 📁.
    /// </summary>
    /// <param name="root">The scratch directory root.</param>
    public DirectorySpillStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    /// <inheritdoc/>
    public string Name => "directory";

    /// <inheritdoc/>
    public Task<ISpillSession> BeginAsync(SpillSessionKey key, CancellationToken ct = default)
    {
        var dir = Path.Combine(this.root, key.ExecutionId.ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(
            dir,
            $"r{key.RegionIndex}-{SanitizeForFile(key.NodeId)}-{Guid.NewGuid():N}.spill");

        return Task.FromResult<ISpillSession>(new Session(path));
    }

    /// <inheritdoc/>
    public Task<int> PurgeExecutionAsync(Guid executionId, CancellationToken ct = default)
    {
        var dir = Path.Combine(this.root, executionId.ToString("N"));
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(0);
        }

        var count = Directory.GetFiles(dir).Length;
        Directory.Delete(dir, recursive: true);
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Guid> ListExecutionsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var dir in Directory.EnumerateDirectories(this.root))
        {
            ct.ThrowIfCancellationRequested();
            if (Guid.TryParseExact(Path.GetFileName(dir), "N", out var id))
            {
                yield return id;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static string SanitizeForFile(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            buffer[i] = Array.IndexOf(invalid, value[i]) >= 0 ? '_' : value[i];
        }

        return new string(buffer);
    }

    private sealed class Session : ISpillSession
    {
        private readonly string path;
        private FileStream? writer;
        private bool disposed;

        public Session(string path)
        {
            this.path = path;
        }

        public long Count { get; private set; }

        public long BytesWritten { get; private set; }

        public async Task AppendAsync(ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            // CopilotNote: DeleteOnClose so an orderly dispose reclaims the file even if the
            // sweeper never runs — belt as well as braces~ 🧹
            this.writer ??= new FileStream(
                this.path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await this.writer.WriteAsync(header, ct).ConfigureAwait(false);
            await this.writer.WriteAsync(payload, ct).ConfigureAwait(false);

            this.Count++;
            this.BytesWritten += payload.Length + 4;
        }

        public async Task FlushAsync(CancellationToken ct = default)
        {
            if (this.writer is not null)
            {
                await this.writer.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        public async IAsyncEnumerable<byte[]> ReadAllAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.writer is null || !File.Exists(this.path))
            {
                yield break;
            }

            await this.writer.FlushAsync(ct).ConfigureAwait(false);

            await using var reader = new FileStream(
                this.path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var header = new byte[4];
            while (true)
            {
                var read = await reader.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct).ConfigureAwait(false);
                if (read < 4)
                {
                    yield break;
                }

                var length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length <= 0)
                {
                    yield break;
                }

                var record = new byte[length];
                await reader.ReadExactlyAsync(record, ct).ConfigureAwait(false);
                yield return record;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            if (this.writer is not null)
            {
                await this.writer.DisposeAsync().ConfigureAwait(false);
                this.writer = null;
            }

            try
            {
                if (File.Exists(this.path))
                {
                    File.Delete(this.path);
                }
            }
            catch (IOException)
            {
                // Best-effort — the sweeper reclaims what dispose misses~ 🧹
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
