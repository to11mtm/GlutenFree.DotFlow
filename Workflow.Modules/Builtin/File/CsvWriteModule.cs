// <copyright file="CsvWriteModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections;
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
using Workflow.Modules.Internal;

/// <summary>
/// 📝 Built-in CSV Write module (<c>builtin.file.csv.write</c>) — writes an array of row
/// dictionaries to a delimited file via CsvHelper~ 📁✨.
/// </summary>
public sealed class CsvWriteModule : IStreamTerminalModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.csv.write";

    /// <inheritdoc />
    public string DisplayName => "Write CSV";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Writes row dictionaries to a CSV/delimited file~ 📝✨";

    /// <inheritdoc />
    public string Icon => "📝";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr.create(
            new PortDefinition("data", "Data", typeof(object), "Array of row dictionaries~ 📄", false),
            PortDefinition.CreateStreaming("items", isRequired: false, description: "Rows to write as a stream — written as they arrive~ 🌊")),
        Outputs: Arr.create(
            new PortDefinition("rowsWritten", "Rows Written", typeof(int), "Number of data rows written~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether the write succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("path", "Path", typeof(string), "Output CSV file path~ 📂", true, null, PropertyEditorType.FilePath, SupportsTemplates: true),
            new ModulePropertyDefinition("data", "Data", typeof(object), "Array of row dictionaries when not connected via port~ 📄", false, null, PropertyEditorType.Json),
            new ModulePropertyDefinition("includeHeader", "Include Header", typeof(bool), "Write a header row~ 🏷️", false, true, PropertyEditorType.Boolean),
            new ModulePropertyDefinition("delimiter", "Delimiter", typeof(string), "Field delimiter (default ,)~ 📊", false, ",", PropertyEditorType.Text),
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

        var rows = NormalizeRows(dataValue);
        if (rows is null)
        {
            return ModuleResult.Fail("📝 data must be an array of row dictionaries~ 💔");
        }

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var includeHeader = FileModuleSupport.GetBool(context.Properties, "includeHeader", true);
        var delimiter = FileModuleSupport.GetString(context.Properties, "delimiter") ?? ",";

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Columns = union of keys in first row (documented), preserving order~ 🏷️
            var columns = rows.Count > 0 ? rows[0].Keys.ToList() : new List<string>();

            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = delimiter };

            using (var writer = new StreamWriter(path, append: false, encoding))
            using (var csv = new CsvWriter(writer, config))
            {
                if (includeHeader && columns.Count > 0)
                {
                    foreach (var col in columns)
                    {
                        csv.WriteField(col);
                    }

                    await csv.NextRecordAsync().ConfigureAwait(false);
                }

                foreach (var row in rows)
                {
                    foreach (var col in columns)
                    {
                        csv.WriteField(row.TryGetValue(col, out var v) ? v?.ToString() ?? string.Empty : string.Empty);
                    }

                    await csv.NextRecordAsync().ConfigureAwait(false);
                }
            }

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["rowsWritten"] = rows.Count,
                ["success"] = true,
            };

            context.Logger.LogDebug("📝 Wrote {Rows} CSV rows to {Path}", rows.Count, path);
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CsvHelperException)
        {
            return ModuleResult.Fail($"📝 Failed to write CSV '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🌊 Phase 5.1.4 — the streaming sink. Rows are written to the file as they arrive, so nothing
    /// is held except the current row. The header comes from the **first** row's keys, which is the
    /// same rule the batch path documents — a stream can't survey every row first without
    /// defeating the point~ 🏷️.
    /// </remarks>
    public async Task<ModuleResult> ExecuteTerminalAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        var rawPath = FileModuleSupport.GetString(context.Properties, "path");
        if (rawPath is null)
        {
            return ModuleResult.Fail("path is required~ 💔");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawPath, PathAccessIntent.Write, out var path, out var failure))
        {
            return failure!;
        }

        if (!EncodingResolver.TryResolve(FileModuleSupport.GetString(context.Properties, "encoding"), out var encoding, out var encErr))
        {
            return ModuleResult.Fail($"🔤 {encErr}~ 💔");
        }

        var includeHeader = FileModuleSupport.GetBool(context.Properties, "includeHeader", true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = FileModuleSupport.GetString(context.Properties, "delimiter") ?? ",",
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var rowsWritten = 0;
            List<string>? columns = null;

            await using (var writer = new StreamWriter(path, append: false, encoding))
            await using (var csv = new CsvWriter(writer, config))
            {
                await foreach (var item in input.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (item.Payload is not JsonPayload json
                        || JsonValueConverter.FromElement(json.Value) is not IReadOnlyDictionary<string, object?> row)
                    {
                        continue;
                    }

                    if (columns is null)
                    {
                        columns = row.Keys.ToList();
                        if (includeHeader && columns.Count > 0)
                        {
                            foreach (var col in columns)
                            {
                                csv.WriteField(col);
                            }

                            await csv.NextRecordAsync().ConfigureAwait(false);
                        }
                    }

                    foreach (var col in columns)
                    {
                        csv.WriteField(row.TryGetValue(col, out var v) ? v?.ToString() ?? string.Empty : string.Empty);
                    }

                    await csv.NextRecordAsync().ConfigureAwait(false);
                    rowsWritten++;
                }
            }

            sw.Stop();
            context.Logger.LogDebug("📝 Streamed {Rows} CSV rows to {Path}", rowsWritten, path);

            return ModuleResult.Ok(
                new Dictionary<string, object?>
                {
                    ["rowsWritten"] = rowsWritten,
                    ["success"] = true,
                },
                ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CsvHelperException)
        {
            return ModuleResult.Fail($"📝 Failed to write CSV '{rawPath}': {ex.Message}~ 💔", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>CSV write is a sink — the region executor calls <see cref="ExecuteTerminalAsync"/>~ 🪣.</remarks>
    public IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
        ModuleExecutionContext context,
        IAsyncEnumerable<StreamItem> input,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "builtin.file.csv.write is a terminal stage — the region executor calls ExecuteTerminalAsync~ 🪣");

    private static List<IReadOnlyDictionary<string, object?>>? NormalizeRows(object? data)
    {
        if (data is null)
        {
            return new List<IReadOnlyDictionary<string, object?>>();
        }

        if (data is not IEnumerable enumerable || data is string)
        {
            return null;
        }

        var result = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var item in enumerable)
        {
            switch (item)
            {
                case IReadOnlyDictionary<string, object?> rod:
                    result.Add(rod);
                    break;
                case IDictionary<string, object?> d:
                    result.Add(new Dictionary<string, object?>(d));
                    break;
                case IDictionary raw:
                    var converted = new Dictionary<string, object?>();
                    foreach (DictionaryEntry entry in raw)
                    {
                        converted[entry.Key?.ToString() ?? string.Empty] = entry.Value;
                    }

                    result.Add(converted);
                    break;
                default:
                    return null;
            }
        }

        return result;
    }
}
