// <copyright file="AzureBlobModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Cloud.Builtin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;
using Workflow.Modules.Cloud.Abstractions;

/// <summary>
/// 🫐 Built-in Azure Blob Storage module (<c>builtin.cloud.azureblob</c>) —
/// upload/download/delete/list/exists against a container~ ☁️✨.
/// </summary>
public sealed class AzureBlobModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.cloud.azureblob";

    /// <inheritdoc />
    public string DisplayName => "Azure Blob Storage";

    /// <inheritdoc />
    public string Category => "Cloud Storage";

    /// <inheritdoc />
    public string Description => "Upload/download/delete/list/exists on Azure Blob Storage~ 🫐✨";

    /// <inheritdoc />
    public string Icon => "🫐";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("success", "Success", typeof(bool), "Whether the operation succeeded~ ✅", false),
            new PortDefinition("blobName", "Blob Name", typeof(string), "The blob operated on~ 🏷️", false),
            new PortDefinition("url", "URL", typeof(string), "Blob URL (upload)~ 🔗", false),
            new PortDefinition("blobs", "Blobs", typeof(object), "List of blobs (list)~ 📋", false),
            new PortDefinition("blobCount", "Blob Count", typeof(int), "Number of listed blobs~ 🔢", false),
            new PortDefinition("exists", "Exists", typeof(bool), "Whether the blob exists (exists)~ ❓", false),
            new PortDefinition("bytesTransferred", "Bytes Transferred", typeof(long), "Bytes up/downloaded~ 📊", false),
            new PortDefinition("durationMs", "Duration (ms)", typeof(long), "Operation duration~ ⏱️", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("operation", "Operation", typeof(string), "upload, download, delete, list, or exists~ 🎯", true, "upload", PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("storageConnectionId", "Storage Connection", typeof(string), "Named storage connection (preferred)~ 📇", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("connectionString", "Connection String", typeof(string), "Inline connection string (dev escape hatch)~ 🔐", false, null, PropertyEditorType.ConnectionString),
            new ModulePropertyDefinition("containerName", "Container", typeof(string), "Blob container name~ 🪣", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("blobName", "Blob Name", typeof(string), "Blob name (required except list)~ 🏷️", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("localPath", "Local Path", typeof(string), "Local file path (upload/download)~ 📂", false, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("prefix", "Prefix", typeof(string), "Name prefix filter (list)~ 🏷️", false, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("createContainer", "Create Container", typeof(bool), "Create the container on upload if missing~ 📁", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        var operation = (GetString(configuration, "operation") ?? string.Empty).ToLowerInvariant();
        if (operation is not ("upload" or "download" or "delete" or "list" or "exists"))
        {
            errors.Add(new ValidationError("INVALID_OPERATION", $"operation '{operation}' must be upload, download, delete, list, or exists~ 💔", PropertyName: "operation"));
        }

        if (GetString(configuration, "containerName") is null)
        {
            errors.Add(new ValidationError("CONTAINER_REQUIRED", "containerName is required~ 💔", PropertyName: "containerName"));
        }

        if (operation is "upload" or "download" && GetString(configuration, "localPath") is null)
        {
            errors.Add(new ValidationError("LOCALPATH_REQUIRED", $"localPath is required for {operation}~ 💔", PropertyName: "localPath"));
        }

        if (operation is "upload" or "download" or "delete" or "exists" && GetString(configuration, "blobName") is null)
        {
            errors.Add(new ValidationError("BLOBNAME_REQUIRED", $"blobName is required for {operation}~ 💔", PropertyName: "blobName"));
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
        var containerName = GetString(context.Properties, "containerName");
        if (containerName is null)
        {
            return ModuleResult.Fail("🫐 containerName is required~ 💔");
        }

        var blobName = GetString(context.Properties, "blobName");
        var sw = Stopwatch.StartNew();

        try
        {
            var service = factory.CreateBlobServiceClient(
                GetString(context.Properties, "storageConnectionId"),
                GetString(context.Properties, "connectionString"));

            var container = service.GetBlobContainerClient(containerName);

            var outputs = operation switch
            {
                "upload" => await this.UploadAsync(context, container, blobName!, cancellationToken).ConfigureAwait(false),
                "download" => await this.DownloadAsync(context, container, blobName!, cancellationToken).ConfigureAwait(false),
                "delete" => await DeleteAsync(container, blobName!, cancellationToken).ConfigureAwait(false),
                "list" => await ListAsync(context, container, cancellationToken).ConfigureAwait(false),
                "exists" => await ExistsAsync(container, blobName!, cancellationToken).ConfigureAwait(false),
                _ => null,
            };

            if (outputs is null)
            {
                return ModuleResult.Fail($"🫐 Unknown operation '{operation}'~ 💔");
            }

            sw.Stop();
            outputs["durationMs"] = sw.ElapsedMilliseconds;
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (ModuleFailure mf)
        {
            return mf.Result;
        }
        catch (RequestFailedException ex)
        {
            return ModuleResult.Fail($"🫐 Azure Blob {operation} failed ({ex.Status}/{ex.ErrorCode}): {ex.Message}~ 💔");
        }
        catch (CloudModuleException ex)
        {
            return ModuleResult.Fail(ex.Message, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"🫐 Azure Blob {operation} local I/O failed: {ex.Message}~ 💔", ex);
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
        ModuleExecutionContext context, BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var localPath = this.ValidatedLocalPath(context, PathAccessIntent.Read);
        if (!System.IO.File.Exists(localPath))
        {
            throw Fail($"🫐 Local file not found: '{localPath}'~ 💔");
        }

        if (GetBool(context.Properties, "createContainer"))
        {
            await container.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        }

        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(localPath, overwrite: true, ct).ConfigureAwait(false);

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["blobName"] = blobName,
            ["url"] = blob.Uri.ToString(),
            ["bytesTransferred"] = new FileInfo(localPath).Length,
        };
    }

    private async Task<Dictionary<string, object?>> DownloadAsync(
        ModuleExecutionContext context, BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var localPath = this.ValidatedLocalPath(context, PathAccessIntent.Write);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var blob = container.GetBlobClient(blobName);
        await blob.DownloadToAsync(localPath, ct).ConfigureAwait(false);

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["blobName"] = blobName,
            ["bytesTransferred"] = new FileInfo(localPath).Length,
        };
    }

    private static async Task<Dictionary<string, object?>> DeleteAsync(BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var deleted = await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["success"] = true, ["blobName"] = blobName, ["exists"] = deleted.Value };
    }

    private static async Task<Dictionary<string, object?>> ListAsync(
        ModuleExecutionContext context, BlobContainerClient container, CancellationToken ct)
    {
        var prefix = GetString(context.Properties, "prefix");
        var blobs = new List<IReadOnlyDictionary<string, object?>>();

        await foreach (var item in container.GetBlobsAsync(prefix: prefix, cancellationToken: ct).ConfigureAwait(false))
        {
            blobs.Add(new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["size"] = item.Properties.ContentLength,
                ["lastModified"] = item.Properties.LastModified,
            });
        }

        return new Dictionary<string, object?>
        {
            ["success"] = true,
            ["blobs"] = blobs,
            ["blobCount"] = blobs.Count,
        };
    }

    private static async Task<Dictionary<string, object?>> ExistsAsync(BlobContainerClient container, string blobName, CancellationToken ct)
    {
        var exists = await container.GetBlobClient(blobName).ExistsAsync(ct).ConfigureAwait(false);
        return new Dictionary<string, object?> { ["success"] = true, ["blobName"] = blobName, ["exists"] = exists.Value };
    }

    private static bool GetBool(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var val) || val is null)
        {
            return false;
        }

        return val switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => false,
        };
    }

    private string ValidatedLocalPath(ModuleExecutionContext context, PathAccessIntent intent)
    {
        var raw = GetString(context.Properties, "localPath") ?? throw Fail("🫐 localPath is required~ 💔");
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
