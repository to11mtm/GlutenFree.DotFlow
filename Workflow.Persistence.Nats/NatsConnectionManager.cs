// <copyright file="NatsConnectionManager.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats;

using System.Diagnostics;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;

/// <summary>
/// 🔗 Manages the NATS connection, JetStream context, and KV context lifecycle~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: NATS.Net 2.x handles automatic reconnects internally. Configure
/// <see cref="NatsOpts"/> with TLS options via the <see cref="NatsConnectionManager(string, NatsOpts?)"/>
/// overload. Connection URL format: <c>nats://user:pass@host:4222</c>~ 🔗
/// </remarks>
public sealed class NatsConnectionManager : IAsyncDisposable
{
    private readonly NatsConnection _connection;
    private readonly NatsJSContext _jsContext;
    private readonly NatsKVContext _kvContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="NatsConnectionManager"/> class~ 🔌.
    /// </summary>
    /// <param name="natsUrl">
    /// NATS server URL, e.g. <c>nats://localhost:4222</c> or <c>nats://user:pass@host:4222</c>.
    /// Multiple URLs comma-separated are supported for cluster fallback~ 🌐.
    /// </param>
    /// <param name="extraOpts">
    /// Optional additional <see cref="NatsOpts"/> to merge (e.g. TLS config). Url is always overridden~ ✨.
    /// </param>
    public NatsConnectionManager(string natsUrl, NatsOpts? extraOpts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(natsUrl);

        var opts = (extraOpts ?? NatsOpts.Default) with { Url = natsUrl };
        _connection = new NatsConnection(opts);
        _jsContext = new NatsJSContext(_connection);
        _kvContext = new NatsKVContext(_jsContext);
    }

    /// <summary>Gets the raw NATS connection~ 🔌.</summary>
    public NatsConnection Connection => _connection;

    /// <summary>Gets the JetStream context for stream operations~ ⚡.</summary>
    public INatsJSContext JetStream => _jsContext;

    /// <summary>Gets the Key-Value store context for KV bucket operations~ 🗃️.</summary>
    public INatsKVContext KV => _kvContext;

    /// <summary>
    /// Pings the server and returns latency. Useful for health checks~ 🏥.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Round-trip latency.</returns>
    public async Task<TimeSpan> PingAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        await _connection.PingAsync(ct).ConfigureAwait(false);
        sw.Stop();
        return sw.Elapsed;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
        => await _connection.DisposeAsync().ConfigureAwait(false);
}

