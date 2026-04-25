// <copyright file="S3Configuration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.S3;

/// <summary>
/// ⚙️ Configuration for the <see cref="S3BlobStore"/> / <see cref="S3PersistenceProvider"/>~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: Set <see cref="EndpointUrl"/> + <see cref="UsePathStyle"/> = <c>true</c> for MinIO
/// or any other S3-compatible self-hosted server. Leave <see cref="EndpointUrl"/> null to talk to
/// real AWS S3, in which case <see cref="Region"/> is required~ ☁️
/// </remarks>
public sealed class S3Configuration
{
    /// <summary>Gets or sets the AWS access key (or MinIO root user)~ .</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the AWS secret key (or MinIO root password)~ .</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the AWS region (e.g. <c>us-east-1</c>). Required for AWS, optional for MinIO~ .</summary>
    public string? Region { get; set; }

    /// <summary>Gets or sets the bucket that all blobs will be stored in~ .</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the custom endpoint URL (for MinIO/LocalStack/etc.). Leave <c>null</c> for AWS S3~ .
    /// </summary>
    public string? EndpointUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use path-style addressing (<c>http://host/bucket/key</c>)
    /// instead of virtual-hosted style. Required for MinIO and most local S3 servers~ ️.
    /// </summary>
    public bool UsePathStyle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable AES-256 server-side encryption for
    /// <see cref="S3BlobStore.PutAsync"/> uploads~ .
    /// </summary>
    public bool ServerSideEncryption { get; set; }

    /// <summary>
    /// Gets or sets the threshold (bytes) above which uploads are sent via multipart. Default 5 MiB~ .
    /// </summary>
    public long MultipartThresholdBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Gets or sets the maximum retry count for transient S3 errors (503, 429)~ .</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Gets or sets the base delay between retry attempts. Default 200 ms (exponential)~ ⏱️.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Validates the configuration. Throws <see cref="InvalidOperationException"/> if invalid~ ✅.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            throw new InvalidOperationException("S3Configuration.AccessKey is required~ ");
        }

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            throw new InvalidOperationException("S3Configuration.SecretKey is required~ ");
        }

        if (string.IsNullOrWhiteSpace(BucketName))
        {
            throw new InvalidOperationException("S3Configuration.BucketName is required~ ");
        }

        if (EndpointUrl is null && string.IsNullOrWhiteSpace(Region))
        {
            throw new InvalidOperationException(
                "S3Configuration.Region is required when EndpointUrl is null (real AWS S3)~ ");
        }

        if (MultipartThresholdBytes < 5 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "S3 multipart minimum part size is 5 MiB — MultipartThresholdBytes must be ≥ 5 MiB~ ");
        }
    }
}
