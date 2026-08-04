// <copyright file="JsonReadModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;
using Workflow.Modules.Internal;

/// <summary>
/// ðŸ“„ Built-in JSON Read module (<c>builtin.file.json.read</c>) â€” parses a JSON file into a
/// plain CLR object graph~ ðŸ“âœ¨.
/// </summary>
public sealed class JsonReadModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.json.read";

    /// <inheritdoc />
    public string DisplayName => "Read JSON";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Parses a JSON file into an object graph~ ðŸ“„âœ¨";

    /// <inheritdoc />
    public string Icon => "ðŸ“„";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition(
                Name: "input",
                DisplayName: "Input",
                DataType: typeof(object),
                Description: "Optional. Connect a previous step to run this one after it; the value is available to templates as {{input}}~ 🔌",
                IsRequired: false)),
        Outputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Parsed JSON as dict/list/scalar~ ðŸ“„", false),
            new PortDefinition("isArray", "Is Array", typeof(bool), "Whether the root is an array~ ðŸ”¢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the parse succeeded~ âœ…", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "JSON file path. Supports {{Variable.Name}}~ ðŸ“‚", true, null, PropertyEditorType.FilePath, SupportsTemplates: true),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding~ ðŸ”¤", false, "utf-8", PropertyEditorType.Text)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        if (FileModuleSupport.GetString(configuration, "path") is null)
        {
            return ValidationResult.Failure(new ValidationError("PATH_REQUIRED", "path is required~ ðŸ’”", PropertyName: "path"));
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
            return ModuleResult.Fail("path is required~ ðŸ’”");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Read, out var path, out var failure))
        {
            return failure!;
        }

        if (!System.IO.File.Exists(path))
        {
            return ModuleResult.Fail($"ðŸ“„ File not found: '{rawPath}'~ ðŸ’”");
        }

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"ðŸ”¤ {encErr}~ ðŸ’”");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var text = await System.IO.File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(text);
            var data = JsonValueConverter.ToClr(node);

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["data"] = data,
                ["isArray"] = node is JsonArray,
                ["success"] = true,
            };

            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (JsonException ex)
        {
            return ModuleResult.Fail($"ðŸ“„ Invalid JSON in '{rawPath}' at line {ex.LineNumber}, pos {ex.BytePositionInLine}: {ex.Message}~ ðŸ’”", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModuleResult.Fail($"ðŸ“„ Failed to read JSON '{rawPath}': {ex.Message}~ ðŸ’”", ex);
        }
    }
}
