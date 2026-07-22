// <copyright file="FileWriteModule.cs" company="GlutenFree">
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
/// ✍️ Built-in File Write module (<c>builtin.file.write</c>) — writes text or binary content
/// to a local file with overwrite / append / create-new semantics~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.1. Paths validate with <see cref="PathAccessIntent.Write"/> so the
/// blocked-extension policy applies. Content may arrive via the <c>content</c> input port or the
/// <c>content</c> property (the port wins)~ 🛡️.
/// </remarks>
public sealed class FileWriteModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.write";

    /// <inheritdoc />
    public string DisplayName => "Write File";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Writes text or binary content to a local file~ ✍️✨";

    /// <inheritdoc />
    public string Icon => "✍️";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("content", "Content", typeof(object), "Content to write (string or byte[])~ 📄", false)),
        Outputs: Arr.create(
            new PortDefinition("bytesWritten", "Bytes Written", typeof(long), "Number of bytes written~ 📊", false),
            new PortDefinition("fullPath", "Full Path", typeof(string), "Resolved absolute path~ 📂", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the write succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "File path to write. Supports {{Variable.Name}}~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("content", "Content", typeof(object), "Content to write when not connected via port~ 📄", false, null, PropertyEditorType.MultilineText),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding (utf-8, utf-16, ascii, latin1)~ 🔤", false, "utf-8", PropertyEditorType.Text),
            new ModulePropertyDefinition("mode", "Mode", typeof(string), "overwrite, append, or createNew~ 📝", false, "overwrite", PropertyEditorType.Dropdown, Arr.create<object>("overwrite", "append", "createNew")),
            new ModulePropertyDefinition("createDirectory", "Create Directory", typeof(bool), "Create the parent directory if missing~ 📁", false, true, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        if (FileModuleSupport.GetString(configuration, "path") is null)
        {
            errors.Add(new ValidationError("PATH_REQUIRED", "path is required~ 💔", PropertyName: "path"));
        }

        var mode = FileModuleSupport.GetString(configuration, "mode") ?? "overwrite";
        if (mode is not ("overwrite" or "append" or "createNew"))
        {
            errors.Add(new ValidationError("INVALID_MODE", $"mode '{mode}' must be overwrite, append, or createNew~ 💔", PropertyName: "mode"));
        }

        var encoding = FileModuleSupport.GetString(configuration, "encoding");
        if (encoding is not null && !EncodingResolver.TryResolve(encoding, out _, out var encErr))
        {
            errors.Add(new ValidationError("INVALID_ENCODING", $"{encErr}~ 💔", PropertyName: "encoding"));
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

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Write, out var path, out var failure))
        {
            return failure!;
        }

        // Port value wins over property~ 🔌
        var contentValue = context.Inputs.TryGetValue("content", out var portVal) && portVal is not null
            ? portVal
            : context.Properties.TryGetValue("content", out var propVal) ? propVal : null;

        if (contentValue is null)
        {
            return ModuleResult.Fail("✍️ No content provided (connect the content port or set the content property)~ 💔");
        }

        var mode = FileModuleSupport.GetString(context.Properties, "mode") ?? "overwrite";
        var createDirectory = FileModuleSupport.GetBool(context.Properties, "createDirectory", true);
        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var isBinary = contentValue is byte[];
        if (isBinary && mode == "append")
        {
            return ModuleResult.Fail("✍️ append mode is text-only in V1 (binary content cannot be appended)~ 💔");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            if (mode == "createNew" && System.IO.File.Exists(path))
            {
                return ModuleResult.Fail($"✍️ createNew failed: file already exists at '{rawPath}'~ 💔");
            }

            if (createDirectory)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            long bytesWritten;
            if (isBinary)
            {
                var bytes = (byte[])contentValue;
                await System.IO.File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                bytesWritten = bytes.LongLength;
            }
            else
            {
                var text = contentValue as string ?? contentValue.ToString() ?? string.Empty;
                if (mode == "append")
                {
                    await System.IO.File.AppendAllTextAsync(path, text, encoding, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await System.IO.File.WriteAllTextAsync(path, text, encoding, cancellationToken).ConfigureAwait(false);
                }

                bytesWritten = encoding.GetByteCount(text);
            }

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["bytesWritten"] = bytesWritten,
                ["fullPath"] = path,
                ["success"] = true,
            };

            context.Logger.LogDebug("✍️ Wrote {Bytes} bytes to {Path} (mode={Mode})", bytesWritten, path, mode);
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"✍️ Failed to write '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }
}
