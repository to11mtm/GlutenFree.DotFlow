// <copyright file="S3Module.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Builtin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;
using Workflow.Modules.Cloud.Abstractions;

/// <summary>
/// 🪣 Built-in Amazon S3 module (<c>builtin.cloud.s3</c>) — upload/download/delete/list/exists
/// against an S3-compatible endpoint~ ☁️✨.
/// </summary>
public sealed class S3Module : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.cloud.s3";

    /// <inheritdoc />
    public string DisplayName => "Amazon S3";

    /// <inheritdoc />
    public string Category => "Cloud Storage";

    /// <inheritdoc />
    public string Description => "Upload/download/delete/list/exists on Amazon S3~ 🪣✨";

    /// <inheritdoc />
    public string Icon => "🪣";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("success", "Success", typeof(bool), "Whether the operation succeeded~ ✅", false),
            new PortDefinition("key", "Key", typeof(string), "The object key operated on~ 🔑", false),
            new PortDefinition("url", "URL", typeof(string), "Virtual-host URL (upload)~ 🔗", false),
            new PortDefinition("objects", "Objects", typeof(object), "List of objects (list)~ 📋", false),
            new PortDefinition("objectCount", "Object Count", typeof(int), "Number of listed objects~ 🔢", false),
            new PortDefinition("exists", "Exists", typeof(bool), "Whether the object exists (exists)~ ❓", false),
            new PortDefinition("bytesTransferred", "Bytes Transferred", typeof(long), "Bytes up/downloaded~ 📊", false),
            new PortDefinition("durationMs", "Duration (ms)", typeof(long), "Operation duration~ ⏱️", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("operation", "Operation", typeof(string), "upload, download, delete, list, or exists~ 🎯", true, "upload", PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("storageConnectionId", "Storage Connection", typeof(string), "Named storage connection (preferred)~ 📇", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("accessKey", "Access Key", typeof(string), "Inline access key (dev escape hatch)~ 🔑", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("secretKey", "Secret Key", typeof(string), "Inline secret key (dev escape hatch)~ 🔐", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("region", "Region", typeof(string), "AWS region~ 🌍", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("serviceUrl", "Service URL", typeof(string), "Custom endpoint (MinIO/on-prem)~ 🔌", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("bucket", "Bucket", typeof(string), "S3 bucket name~ 🪣", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("key", "Key", typeof(string), "Object key (required except list)~ 🔑", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("localPath", "Local Path", typeof(string), "Local file path (upload/download)~ 📂", false, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("prefix", "Prefix", typeof(string), "Key prefix filter (list)~ 🏷️", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("maxKeys", "Max Keys", typeof(int), "Max objects to list~ 🔢", false, 1000, PropertyEditorType.Number),
            new ModulePropertyDefinition("contentType", "Content Type", typeof(string), "MIME type (upload)~ 📄", false, null, PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        var operation = (GetString(configuration, "operation") ?? string.Empty).ToLowerInvariant();
        if (operation is not ("upload" or "download" or "delete" or "list" or "exists"))
        {
            errors.Add(new ValidationError("INVALID_OPERATION", $"operation '{operation}' must be upload, download, delete, list, or exists~ 💔", PropertyName: "operation"));
        }

        if (GetString(configuration, "bucket") is null)
        {
            errors.Add(new ValidationError("BUCKET_REQUIRED", "bucket is required~ 💔", PropertyName: "bucket"));
        }

        if (operation is "upload" or "download" && GetString(configuration, "localPath") is null)
        {
            errors.Add(new ValidationError("LOCALPATH_REQUIRED", $"localPath is required for {operation}~ 💔", PropertyName: "localPath"));
        }

        if (operation is "upload" or "download" or "delete" or "exists" && GetString(configuration, "key") is null)
        {
            errors.Add(new ValidationError("KEY_REQUIRED", $"key is required for {operation}~ 💔", PropertyName: "key"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var factory = context.Services.GetService<IStorageClientFactory>();
        if (factory is null)
        {
            return ModuleResult.Fail("☁️ IStorageClientFactory is not registered — call AddCloudStorageModules() at host startup~ 💔");
        }

        var operation = (GetString(context.Properties, "operation") ?? "upload").ToLowerInvariant();
        var bucket = GetString(context.Properties, "bucket");
        if (bucket is null)
        {
            return ModuleResult.Fail("🪣 bucket is required~ 💔");
        }

        var key = GetString(context.Properties, "key");
        var sw = Stopwatch.StartNew();

        try
        {
            using var s3 = factory.CreateS3Client(
                GetString(context.Properties, "storageConnectionId"),
                GetString(context.Properties, "accessKey"),
                GetString(context.Properties, "secretKey"),
                GetString(context.Properties, "region"),
                GetString(context.Properties, "serviceUrl"));

            var outputs = operation switch
            {
                "upload" => await this.UploadAsync(context, s3, bucket, key!, cancellationToken).ConfigureAwait(false),
                "download" => await this.DownloadAsync(context, s3, bucket, key!, cancellationToken).ConfigureAwait(false),
                "delete" => await DeleteAsync(s3, bucket, key!, cancellationToken).ConfigureAwait(false),
                "list" => await ListAsync(context, s3, bucket, cancellationToken).ConfigureAwait(false),
                "exists" => await ExistsAsync(s3, bucket, key!, cancellationToken).ConfigureAwait(false),
                _ => null,
            };

            if (outputs is null)
            {
                return ModuleResult.Fail($"🪣 Unknown operation '{operation}'~ 💔");
            }

            sw.Stop();
            outputs["durationMs"] = sw.ElapsedMilliseconds;
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (ModuleFailure mf)
        {
            return mf.Result;
        }
        catch (CloudModuleException ex)
        {
            // Unknown/disabled connection or unknown kind — surface as a friendly failure~ 🛡️
            return ModuleResult.Fail(ex.Message, ex);
        }
        catch (AmazonS3Exception ex)
        {
            // Never leak credentials — surface status + error code only~ 🛡️
            return ModuleResult.Fail($"🪣 S3 {operation} failed ({ex.StatusCode}/{ex.ErrorCode}): {ex.Message}~ 💔");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"🪣 S3 {operation} local I/O failed: {ex.Message}~ 💔", ex);
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return null;
        }

        var s = val as string ?? val.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private async Task<Dictionary<string, object?>> UploadAsync(
        ModuleExecutionContext context, IAmazonS3 s3, string bucket, string key, CancellationToken ct)
    {
        var localPath = this.ValidatedLocalPath(context, PathAccessIntent.Read);
        if (!System.IO.File.Exists(localPath))
        {
            throw Fail($"🪣 Local file not found: '{localPath}'~ 💔");
        }

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            FilePath = localPath,
        };

        var contentType = GetString(context.Properties, "contentType");
        if (contentType is not null)
        {
            request.ContentType = contentType;
        }

        await s3.PutObjectAsync(request, ct).ConfigureAwait(false);
        var bytes = new FileInfo(localPath).Length;

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["key"] = key,
            ["url"] = BuildObjectUrl(s3.Config, bucket, key),
            ["bytesTransferred"] = bytes,
        };
    }

    private static string BuildObjectUrl(IClientConfig config, string bucket, string key)
    {
        if (!string.IsNullOrWhiteSpace(config.ServiceURL))
        {
            return $"{config.ServiceURL.TrimEnd('/')}/{bucket}/{key}";
        }

        var region = config.RegionEndpoint?.SystemName ?? "us-east-1";
        return $"https://{bucket}.s3.{region}.amazonaws.com/{key}";
    }

    private async Task<Dictionary<string, object?>> DownloadAsync(
        ModuleExecutionContext context, IAmazonS3 s3, string bucket, string key, CancellationToken ct)
    {
        var localPath = this.ValidatedLocalPath(context, PathAccessIntent.Write);

        using var response = await s3.GetObjectAsync(bucket, key, ct).ConfigureAwait(false);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await response.WriteResponseStreamToFileAsync(localPath, append: false, ct).ConfigureAwait(false);

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["key"] = key,
            ["bytesTransferred"] = new FileInfo(localPath).Length,
        };
    }

    private static async Task<Dictionary<string, object?>> DeleteAsync(IAmazonS3 s3, string bucket, string key, CancellationToken ct)
    {
        await s3.DeleteObjectAsync(bucket, key, ct).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["success"] = true, ["key"] = key };
    }

    private static async Task<Dictionary<string, object?>> ListAsync(
        ModuleExecutionContext context, IAmazonS3 s3, string bucket, CancellationToken ct)
    {
        var maxKeys = context.Properties.TryGetValue("maxKeys", out var mk) && mk is not null && int.TryParse(mk.ToString(), out var parsed) ? parsed : 1000;
        var request = new ListObjectsV2Request
        {
            BucketName = bucket,
            Prefix = GetString(context.Properties, "prefix"),
            MaxKeys = maxKeys,
        };

        var response = await s3.ListObjectsV2Async(request, ct).ConfigureAwait(false);
        var objects = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var o in response.S3Objects)
        {
            objects.Add(new Dictionary<string, object?>
            {
                ["key"] = o.Key,
                ["size"] = o.Size,
                ["lastModified"] = o.LastModified,
                ["etag"] = o.ETag,
            });
        }

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["objects"] = objects,
            ["objectCount"] = objects.Count,
        };
    }

    private static async Task<Dictionary<string, object?>> ExistsAsync(IAmazonS3 s3, string bucket, string key, CancellationToken ct)
    {
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key, ct).ConfigureAwait(false);
            return new Dictionary<string, object?> { ["success"] = true, ["key"] = key, ["exists"] = true };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new Dictionary<string, object?> { ["success"] = true, ["key"] = key, ["exists"] = false };
        }
    }

    private string ValidatedLocalPath(ModuleExecutionContext context, PathAccessIntent intent)
    {
        var raw = GetString(context.Properties, "localPath") ?? throw Fail("🪣 localPath is required~ 💔");
        if (!FileModuleSupport.TryValidatePath(context, raw, intent, out var resolved, out var failure))
        {
            throw new ModuleFailure(failure!);
        }

        return resolved;
    }

    private static ModuleFailure Fail(string message) => new(ModuleResult.Fail(message));

    /// <summary>
    /// Internal control-flow exception carrying a ready <see cref="ModuleResult"/>~ 🧯.
    /// </summary>
    private sealed class ModuleFailure : Exception
    {
        public ModuleFailure(ModuleResult result) => this.Result = result;

        public ModuleResult Result { get; }
    }
}
