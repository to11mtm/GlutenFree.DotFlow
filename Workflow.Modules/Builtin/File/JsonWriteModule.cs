// <copyright file="JsonWriteModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 💾 Built-in JSON Write module (<c>builtin.file.json.write</c>) — serialises an object graph
/// to a JSON file~ 📁✨.
/// </summary>
public sealed class JsonWriteModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.json.write";

    /// <inheritdoc />
    public string DisplayName => "Write JSON";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Serialises an object graph to a JSON file~ 💾✨";

    /// <inheritdoc />
    public string Icon => "💾";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Object graph to serialise~ 📄", false)),
        Outputs: Arr.create(
            new PortDefinition("bytesWritten", "Bytes Written", typeof(long), "Number of bytes written~ 📊", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the write succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "Output JSON file path~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("data", "Data", typeof(object), "Object graph when not connected via port~ 📄", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("indented", "Indented", typeof(bool), "Pretty-print with indentation~ 🎨", false, true, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding~ 🔤", false, "utf-8", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (FileModuleSupport.GetString(configuration, "path") is null)
        {
            return ValidationResult.Failure(new ValidationError("PATH_REQUIRED", "path is required~ 💔", PropertyName: "path"));
        }

        return ValidationResult.Success();
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

        var dataValue = context.Inputs.TryGetValue("data", out var portVal) && portVal is not null
            ? portVal
            : context.Properties.TryGetValue("data", out var propVal) ? propVal : null;

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var indented = FileModuleSupport.GetBool(context.Properties, "indented", true);

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = indented };
            var json = JsonSerializer.Serialize(dataValue, options);
            await System.IO.File.WriteAllTextAsync(path, json, encoding, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["bytesWritten"] = (long)encoding.GetByteCount(json),
                ["success"] = true,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (JsonException ex)
        {
            return ModuleResult.Fail($"💾 Could not serialise data to JSON: {ex.Message}~ 💔", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"💾 Failed to write JSON '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }
}
