// <copyright file="FileReadModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 📖 Built-in File Read module (<c>builtin.file.read</c>) — reads a local file as text,
/// binary, or an array of lines, with encoding + size-limit support~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.1. All paths go through <see cref="IWorkflowPathValidator"/>
/// (resolved from <c>context.Services</c>) — never touch the raw path directly~ 🛡️.
/// </remarks>
public sealed class FileReadModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.read";

    /// <inheritdoc />
    public string DisplayName => "Read File";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Reads a local file as text, binary, or lines~ 📖✨";

    /// <inheritdoc />
    public string Icon => "📖";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("content", "Content", typeof(object), "File content (string, byte[], or string[])~ 📄", false),
            new PortDefinition("size", "Size (bytes)", typeof(long), "File size in bytes~ 📊", false),
            new PortDefinition("lastModified", "Last Modified", typeof(DateTimeOffset), "Last write time (UTC)~ 🕒", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the read succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "File path to read. Supports {{Variable.Name}}~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding (utf-8, utf-16, ascii, latin1)~ 🔤", false, "utf-8", PropertyEditorType.Text),
            new ModulePropertyDefinition("readAs", "Read As", typeof(string), "text, binary, or lines~ 📄", false, "text", PropertyEditorType.Dropdown, Arr.create<object>("text", "binary", "lines")),
            new ModulePropertyDefinition("maxSize", "Max Size (bytes)", typeof(long), "Max file size; exceeding fails the read~ 🧠", false, null, PropertyEditorType.Number)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        if (FileModuleSupport.GetString(configuration, "path") is null)
        {
            errors.Add(new ValidationError("PATH_REQUIRED", "path is required~ 💔", PropertyName: "path"));
        }

        var readAs = FileModuleSupport.GetString(configuration, "readAs") ?? "text";
        if (readAs is not ("text" or "binary" or "lines"))
        {
            errors.Add(new ValidationError("INVALID_READ_AS", $"readAs '{readAs}' must be text, binary, or lines~ 💔", PropertyName: "readAs"));
        }

        var encoding = FileModuleSupport.GetString(configuration, "encoding");
        if (encoding is not null && !EncodingResolver.TryResolve(encoding, out _, out var encErr))
        {
            errors.Add(new ValidationError("INVALID_ENCODING", $"{encErr}~ 💔", PropertyName: "encoding"));
        }

        var maxSize = FileModuleSupport.TryGetLong(configuration, "maxSize");
        if (maxSize is <= 0)
        {
            errors.Add(new ValidationError("INVALID_MAX_SIZE", "maxSize must be positive~ 💔", PropertyName: "maxSize"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var rawPath = FileModuleSupport.GetString(context.Properties, "path");
        if (rawPath is null)
        {
            return ModuleResult.Fail("path is required~ 💔");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Read, out var path, out var failure))
        {
            return failure!;
        }

        if (!System.IO.File.Exists(path))
        {
            return ModuleResult.Fail($"📖 File not found: '{rawPath}'~ 💔");
        }

        var readAs = FileModuleSupport.GetString(context.Properties, "readAs") ?? "text";
        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var options = context.Services.GetFileSystemOptions();
        var maxSize = FileModuleSupport.TryGetLong(context.Properties, "maxSize") ?? options.DefaultMaxReadBytes;

        var sw = Stopwatch.StartNew();
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maxSize)
            {
                return ModuleResult.Fail(
                    $"🧠 File is {info.Length} bytes, exceeds maxSize {maxSize}~ 📏",
                    new FileTooLargeException(info.Length, maxSize));
            }

            object content = readAs switch
            {
                "binary" => await System.IO.File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false),
                "lines" => await System.IO.File.ReadAllLinesAsync(path, encoding, cancellationToken).ConfigureAwait(false),
                _ => await System.IO.File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false),
            };

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["content"] = content,
                ["size"] = info.Length,
                ["lastModified"] = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                ["success"] = true,
            };

            context.Logger.LogDebug("📖 Read {Bytes} bytes from {Path}", info.Length, path);
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"📖 Failed to read '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }
}
