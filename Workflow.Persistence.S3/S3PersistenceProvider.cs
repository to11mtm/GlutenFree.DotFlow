// <copyright file="S3PersistenceProvider.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.S3;

using System.Diagnostics;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// ☁️ S3-backed persistence provider — supplies <see cref="IBlobStore"/> only~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: S3 is a blob store, not a database. The relational repository properties
/// (<see cref="Workflows"/>, <see cref="ExecutionHistory"/>, <see cref="Variables"/>) throw
/// <see cref="NotSupportedException"/>. Compose this provider with Sqlite/Postgres/NATS via
/// <c>CompositePersistenceProvider</c> for full-stack persistence~
/// </remarks>
public sealed class S3PersistenceProvider : IPersistenceProvider
{
    private readonly S3Configuration _config;
    private readonly IAmazonS3 _client;
    private readonly bool _ownsClient;
    private S3BlobStore? _blobs;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3PersistenceProvider"/> class~ ☁️.
    /// </summary>
    /// <param name="config">The S3 configuration.</param>
    public S3PersistenceProvider(S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;
        _client = S3BlobStore.CreateClient(config);
        _ownsClient = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="S3PersistenceProvider"/> class with an
    /// externally-managed client~ ☁️.
    /// </summary>
    /// <param name="client">A pre-configured S3 client.</param>
    /// <param name="config">The S3 configuration.</param>
    public S3PersistenceProvider(IAmazonS3 client, S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;
        _client = client;
        _ownsClient = false;
    }

    /// <inheritdoc/>
    public string ProviderName => "s3";

    /// <inheritdoc/>
    public bool IsInitialized => _initialized;

    /// <inheritdoc/>
    public IWorkflowRepository Workflows
        => throw new NotSupportedException("S3 provider does not support workflow persistence — compose with a SQL or NATS provider~ ");

    /// <inheritdoc/>
    public IExecutionHistoryRepository ExecutionHistory
        => throw new NotSupportedException("S3 provider does not support execution history persistence — compose with a SQL or NATS provider~ ");

    /// <inheritdoc/>
    public IVariableStore Variables
        => throw new NotSupportedException("S3 provider does not support variable persistence — compose with a SQL or NATS provider~ ");

    /// <inheritdoc/>
    public IBlobStore? Blobs => _blobs;

    /// <inheritdoc/>
    /// <remarks>Creates the configured bucket if it doesn't already exist~ .</remarks>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var exists = await BucketExistsAsync(ct).ConfigureAwait(false);
        if (!exists)
        {
            var req = new PutBucketRequest
            {
                BucketName = _config.BucketName,
                UseClientRegion = true,
            };
            await _client.PutBucketAsync(req, ct).ConfigureAwait(false);
        }

        _blobs = new S3BlobStore(_client, _config);
        _initialized = true;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _client
                .GetACLAsync(new GetACLRequest { BucketName = _config.BucketName }, ct)
                .ConfigureAwait(false);
            sw.Stop();
            return new HealthCheckResult(true, ProviderName, sw.Elapsed);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            sw.Stop();
            return new HealthCheckResult(
                false,
                ProviderName,
                sw.Elapsed,
                $"Bucket '{_config.BucketName}' not found~ ");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckResult(false, ProviderName, sw.Elapsed, ex.Message);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _blobs?.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<bool> BucketExistsAsync(CancellationToken ct)
    {
        try
        {
            // CopilotNote: HeadBucket is the canonical existence check; HEAD bucket returns
            // 404 when missing, 200 when accessible, 403 when present-but-forbidden~
            await _client
                .GetACLAsync(new GetACLRequest { BucketName = _config.BucketName }, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound
                                            || string.Equals(ex.ErrorCode, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }
}
