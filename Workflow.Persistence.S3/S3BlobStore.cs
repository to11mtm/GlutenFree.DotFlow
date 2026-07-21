// <copyright file="S3BlobStore.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.S3;

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Workflow.Persistence.Abstractions;

/// <summary>
/// ☁️ S3-backed implementation of <see cref="IBlobStore"/>~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: Works with real AWS S3 and S3-compatible servers (MinIO, LocalStack, Ceph, etc.).
/// Uploads ≤ <see cref="S3Configuration.MultipartThresholdBytes"/> use a single PUT; larger uploads
/// stream through <see cref="TransferUtility"/> for automatic multipart chunking. Transient
/// errors (HTTP 503, 429) are retried with exponential backoff~
/// </remarks>
public sealed class S3BlobStore : IBlobStore, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly S3Configuration _config;
    private readonly TransferUtility _transferUtility;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="S3BlobStore"/> class with a managed client~ ☁️.
    /// </summary>
    /// <param name="config">The S3 configuration.</param>
    public S3BlobStore(S3Configuration config)
        : this(CreateClient(config), config, ownsClient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="S3BlobStore"/> class with an externally-managed client~ ☁️.
    /// </summary>
    /// <param name="client">A pre-configured S3 client.</param>
    /// <param name="config">The S3 configuration.</param>
    public S3BlobStore(IAmazonS3 client, S3Configuration config)
        : this(client, config, ownsClient: false)
    {
    }

    private S3BlobStore(IAmazonS3 client, S3Configuration config, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _client = client;
        _config = config;
        _transferUtility = new TransferUtility(_client);
        _ownsClient = ownsClient;
    }

    /// <summary>Gets the underlying S3 client (for advanced use)~ .</summary>
    public IAmazonS3 Client => _client;

    /// <summary>Gets the configured bucket name~ .</summary>
    public string BucketName => _config.BucketName;

    /// <inheritdoc/>
    public async Task<string> PutAsync(
        string key,
        Stream data,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(data);

        contentType ??= GuessContentType(key);
        var useMultipart = !data.CanSeek || data.Length > _config.MultipartThresholdBytes;

        if (!useMultipart)
        {
            return await RetryAsync(
                async innerCt =>
                {
                    var put = new PutObjectRequest
                    {
                        BucketName = _config.BucketName,
                        Key = key,
                        InputStream = data,
                        ContentType = contentType,
                        AutoCloseStream = false,
                        AutoResetStreamPosition = false,
                    };

                    if (_config.ServerSideEncryption)
                    {
                        put.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                    }

                    var resp = await _client.PutObjectAsync(put, innerCt).ConfigureAwait(false);
                    return resp.ETag ?? string.Empty;
                },
                ct).ConfigureAwait(false);
        }

        // Multipart path — TransferUtility handles chunking automatically~
        var upload = new TransferUtilityUploadRequest
        {
            BucketName = _config.BucketName,
            Key = key,
            InputStream = data,
            ContentType = contentType,
            AutoCloseStream = false,
            PartSize = _config.MultipartThresholdBytes,
        };

        if (_config.ServerSideEncryption)
        {
            upload.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        await RetryAsync(
            async innerCt =>
            {
                await _transferUtility.UploadAsync(upload, innerCt).ConfigureAwait(false);
                return 0; // unused
            },
            ct).ConfigureAwait(false);

        // TransferUtility doesn't surface the ETag — fetch it via HEAD~
        var head = await _client
            .GetObjectMetadataAsync(_config.BucketName, key, ct)
            .ConfigureAwait(false);
        return head.ETag ?? string.Empty;
    }

    /// <inheritdoc/>
    public async Task<Stream?> GetAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var resp = await RetryAsync(
                innerCt => _client.GetObjectAsync(_config.BucketName, key, innerCt),
                ct).ConfigureAwait(false);

            // CopilotNote: ResponseStream is owned by the GetObjectResponse; the response
            // implements IDisposable, but disposing it closes the stream — so we hand the
            // raw stream to the caller and let them dispose it~
            return resp.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var existed = await ExistsAsync(key, ct).ConfigureAwait(false);
        if (!existed)
        {
            return false;
        }

        await RetryAsync(
            async innerCt =>
            {
                await _client.DeleteObjectAsync(_config.BucketName, key, innerCt).ConfigureAwait(false);
                return 0;
            },
            ct).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _client
                .GetObjectMetadataAsync(_config.BucketName, key, ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<string> GeneratePresignedUrlAsync(
        string key,
        TimeSpan expiry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), "Presigned URL expiry must be positive~ ⏱️");
        }

        var req = new GetPreSignedUrlRequest
        {
            BucketName = _config.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET,
        };

        // CopilotNote: GetPreSignedURLAsync is async-shaped but does no IO — returns immediately~
        return _client.GetPreSignedURLAsync(req);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _transferUtility.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    /// <summary>
    /// Builds an <see cref="AmazonS3Client"/> from the given configuration~ .
    /// </summary>
    /// <param name="config">The S3 configuration.</param>
    /// <returns>A configured S3 client.</returns>
    public static IAmazonS3 CreateClient(S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        var clientConfig = new AmazonS3Config
        {
            ForcePathStyle = config.UsePathStyle,
        };

        if (!string.IsNullOrWhiteSpace(config.EndpointUrl))
        {
            clientConfig.ServiceURL = config.EndpointUrl;

            // CopilotNote: When the endpoint is plain HTTP (e.g. local MinIO) we MUST set
            // UseHttp=true — otherwise the AWSSDK signer generates presigned URLs with the
            // https:// scheme which fails TLS handshake against the http server~
            if (config.EndpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                clientConfig.UseHttp = true;
            }

            // CopilotNote: When ServiceURL is set we must NOT also set RegionEndpoint —
            // doing so causes the SDK to ignore the custom URL~
            if (!string.IsNullOrWhiteSpace(config.Region))
            {
                clientConfig.AuthenticationRegion = config.Region;
            }
        }
        else
        {
            clientConfig.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region);
        }

        return new AmazonS3Client(config.AccessKey, config.SecretKey, clientConfig);
    }

    /// <summary>Maps file extensions → content type for nicer Get responses~ ️.</summary>
    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" or ".log" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".csv" => "text/csv",
            _ => "application/octet-stream",
        };
    }

    private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> op, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await op(ct).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex) when (IsTransient(ex) && attempt < _config.MaxRetryAttempts)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(
                    _config.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(AmazonS3Exception ex)
    {
        // CopilotNote: 503 ServiceUnavailable, 429 TooManyRequests, S3 "SlowDown" error code~
        return ex.StatusCode == HttpStatusCode.ServiceUnavailable
            || (int)ex.StatusCode == 429
            || string.Equals(ex.ErrorCode, "SlowDown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.ErrorCode, "RequestTimeout", StringComparison.OrdinalIgnoreCase);
    }
}

