// <copyright file="CsvReadModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 📊 Built-in CSV Read module (<c>builtin.file.csv.read</c>) — parses a delimited file into
/// an array of row dictionaries via CsvHelper~ 📁✨.
/// </summary>
public sealed class CsvReadModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.csv.read";

    /// <inheritdoc />
    public string DisplayName => "Read CSV";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Parses a CSV/delimited file into row dictionaries~ 📊✨";

    /// <inheritdoc />
    public string Icon => "📊";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("rows", "Rows", typeof(object), "Array of row dictionaries~ 📄", false),
            new PortDefinition("rowCount", "Row Count", typeof(int), "Number of data rows~ 🔢", false),
            new PortDefinition("columns", "Columns", typeof(object), "Column names~ 🏷️", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the parse succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "CSV file path. Supports {{Variable.Name}}~ 📂", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("hasHeader", "Has Header", typeof(bool), "Whether the first row is a header~ 🏷️", false, true, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("delimiter", "Delimiter", typeof(string), "Field delimiter (default ,)~ 📊", false, ",", PropertyEditorType.Text),
            new ModulePropertyDefinition("encoding", "Encoding", typeof(string), "Text encoding~ 🔤", false, "utf-8", PropertyEditorType.Text),
            new ModulePropertyDefinition("skipEmptyRows", "Skip Empty Rows", typeof(bool), "Skip fully-empty rows~ 🧹", false, true, PropertyEditorType.Boolean)));

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
    public Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var rawPath = FileModuleSupport.GetString(context.Properties, "path");
        if (rawPath is null)
        {
            return Task.FromResult(ModuleResult.Fail("path is required~ 💔"));
        }

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Read, out var path, out var failure))
        {
            return Task.FromResult(failure!);
        }

        if (!System.IO.File.Exists(path))
        {
            return Task.FromResult(ModuleResult.Fail($"📊 File not found: '{rawPath}'~ 💔"));
        }

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return Task.FromResult(ModuleResult.Fail($"🔤 {encErr}~ 💔"));
        }

        var hasHeader = FileModuleSupport.GetBool(context.Properties, "hasHeader", true);
        var delimiter = FileModuleSupport.GetString(context.Properties, "delimiter") ?? ",";
        var skipEmptyRows = FileModuleSupport.GetBool(context.Properties, "skipEmptyRows", true);

        var sw = Stopwatch.StartNew();
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter,
                HasHeaderRecord = hasHeader,
                DetectColumnCountChanges = false,
                MissingFieldFound = null,
                BadDataFound = null,
            };

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            string[]? headers = null;

            using (var reader = new StreamReader(path, encoding))
            using (var parser = new CsvParser(reader, config))
            {
                while (parser.Read())
                {
                    var record = parser.Record;
                    if (record is null)
                    {
                        continue;
                    }

                    if (skipEmptyRows && record.All(string.IsNullOrEmpty))
                    {
                        continue;
                    }

                    if (hasHeader && headers is null)
                    {
                        headers = record;
                        continue;
                    }

                    headers ??= Enumerable.Range(0, record.Length).Select(i => $"column{i}").ToArray();

                    var row = new Dictionary<string, object?>();
                    for (var i = 0; i < record.Length; i++)
                    {
                        var key = i < headers.Length ? headers[i] : $"column{i}";
                        row[key] = record[i];
                    }

                    rows.Add(row);
                }
            }

            sw.Stop();

            var columns = headers ?? Array.Empty<string>();
            var outputs = new Dictionary<string, object?>
            {
                ["rows"] = rows,
                ["rowCount"] = rows.Count,
                ["columns"] = columns.ToList(),
                ["success"] = true,
            };

            context.Logger.LogDebug("📊 Parsed {Rows} CSV rows from {Path}", rows.Count, path);
            return Task.FromResult(ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CsvHelperException)
        {
            return Task.FromResult(ModuleResult.Fail($"📊 Failed to parse CSV '{rawPath}': {ex.Message}~ 💔", ex));
        }
    }
}
