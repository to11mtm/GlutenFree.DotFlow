// <copyright file="CompressModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// ðŸ—œï¸ Built-in Compress module (<c>builtin.file.compress</c>) â€” creates a Zip / GZip / Tar /
/// TarGz archive from one or more source files/directories using .NET in-box APIs~ ðŸ“âœ¨.
/// </summary>
public sealed class CompressModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.compress";

    /// <inheritdoc />
    public string DisplayName => "Compress Files";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Creates a Zip/GZip/Tar/TarGz archive~ ðŸ—œï¸âœ¨";

    /// <inheritdoc />
    public string Icon => "ðŸ—œï¸";

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
            new PortDefinition("archivePath", "Archive Path", typeof(string), "Path to the created archive~ ðŸ“¦", false),
            new PortDefinition("originalSize", "Original Size", typeof(long), "Total uncompressed bytes~ ðŸ“Š", false),
            new PortDefinition("compressedSize", "Compressed Size", typeof(long), "Archive size in bytes~ ðŸ“‰", false),
            new PortDefinition("compressionRatio", "Compression Ratio", typeof(decimal), "compressed/original~ ðŸ“", false),
            new PortDefinition("fileCount", "File Count", typeof(int), "Number of files archived~ ðŸ”¢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether compression succeeded~ âœ…", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("sourcePath", "Source Path(s)", typeof(object), "File/dir path or array of paths~ ðŸ“‚", true, null, PropertyEditorType.Text),
            new ModulePropertyDefinition("outputPath", "Output Path", typeof(string), "Archive output path~ ðŸ“¦", true, null, PropertyEditorType.FilePath, SupportsTemplates: true),
            new ModulePropertyDefinition("format", "Format", typeof(string), "zip, gzip, tar, or targz~ ðŸ—œï¸", true, "zip", PropertyEditorType.Dropdown, Arr.create<object>("zip", "gzip", "tar", "targz")),
            new ModulePropertyDefinition("compressionLevel", "Compression Level", typeof(string), "optimal, fastest, smallestSize, noCompression~ ðŸ“", false, "optimal", PropertyEditorType.Dropdown, Arr.create<object>("optimal", "fastest", "smallestSize", "noCompression")),
            new ModulePropertyDefinition("includeBaseDirectory", "Include Base Directory", typeof(bool), "Prefix entries with the base dir name~ ðŸ“", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        if (!configuration.ContainsKey("sourcePath"))
        {
            errors.Add(new ValidationError("SOURCE_REQUIRED", "sourcePath is required~ ðŸ’”", PropertyName: "sourcePath"));
        }

        if (FileModuleSupport.GetString(configuration, "outputPath") is null)
        {
            errors.Add(new ValidationError("OUTPUT_REQUIRED", "outputPath is required~ ðŸ’”", PropertyName: "outputPath"));
        }

        var format = (FileModuleSupport.GetString(configuration, "format") ?? "zip").ToLowerInvariant();
        if (format is not ("zip" or "gzip" or "tar" or "targz"))
        {
            errors.Add(new ValidationError("INVALID_FORMAT", $"format '{format}' must be zip, gzip, tar, or targz~ ðŸ’”", PropertyName: "format"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var rawOutput = FileModuleSupport.GetString(context.Properties, "outputPath");
        if (rawOutput is null)
        {
            return ModuleResult.Fail("outputPath is required~ ðŸ’”");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawOutput, PathAccessIntent.Write, out var outputPath, out var outFail))
        {
            return outFail!;
        }

        var format = (FileModuleSupport.GetString(context.Properties, "format") ?? "zip").ToLowerInvariant();
        var level = ParseLevel(FileModuleSupport.GetString(context.Properties, "compressionLevel"));
        var includeBaseDir = FileModuleSupport.GetBool(context.Properties, "includeBaseDirectory", false);

        // Resolve + validate every source path (Read intent)~ ðŸ›¡ï¸
        var rawSources = ExtractSources(context.Properties["sourcePath"]);
        if (rawSources.Count == 0)
        {
            return ModuleResult.Fail("ðŸ—œï¸ No source paths provided~ ðŸ’”");
        }

        var files = new List<(string Full, string Entry)>();
        foreach (var raw in rawSources)
        {
            if (!FileModuleSupport.TryValidatePath(context, raw, PathAccessIntent.Read, out var resolved, out var srcFail))
            {
                return srcFail!;
            }

            if (Directory.Exists(resolved))
            {
                var baseName = includeBaseDir ? Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar)) : null;
                foreach (var f in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(resolved, f).Replace(Path.DirectorySeparatorChar, '/');
                    files.Add((f, baseName is null ? rel : baseName + "/" + rel));
                }
            }
            else if (System.IO.File.Exists(resolved))
            {
                files.Add((resolved, Path.GetFileName(resolved)));
            }
            else
            {
                return ModuleResult.Fail($"ðŸ—œï¸ Source not found: '{raw}'~ ðŸ’”");
            }
        }

        if ((format == "gzip") && files.Count != 1)
        {
            return ModuleResult.Fail("ðŸ—œï¸ gzip supports exactly one source file (use tar/targz for multiple)~ ðŸ’”");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            switch (format)
            {
                case "zip":
                    WriteZip(outputPath, files, level);
                    break;
                case "gzip":
                    await WriteGZipAsync(outputPath, files[0].Full, level, cancellationToken).ConfigureAwait(false);
                    break;
                case "tar":
                    await WriteTarAsync(outputPath, files, gzip: false, cancellationToken).ConfigureAwait(false);
                    break;
                default: // targz
                    await WriteTarAsync(outputPath, files, gzip: true, cancellationToken).ConfigureAwait(false);
                    break;
            }

            sw.Stop();

            var originalSize = files.Sum(f => new FileInfo(f.Full).Length);
            var compressedSize = new FileInfo(outputPath).Length;
            var ratio = originalSize > 0 ? Math.Round((decimal)compressedSize / originalSize, 4) : 0m;

            var outputs = new Dictionary<string, object?>
            {
                ["archivePath"] = outputPath,
                ["originalSize"] = originalSize,
                ["compressedSize"] = compressedSize,
                ["compressionRatio"] = ratio,
                ["fileCount"] = files.Count,
                ["success"] = true,
            };

            context.Logger.LogDebug("ðŸ—œï¸ Compressed {Count} files â†’ {Path} ({Format})", files.Count, outputPath, format);
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ModuleResult.Fail($"ðŸ—œï¸ Compression failed: {ex.Message}~ ðŸ’”", ex);
        }
    }

    private static List<string> ExtractSources(object? value)
    {
        var list = new List<string>();
        switch (value)
        {
            case null:
                break;
            case string s when !string.IsNullOrWhiteSpace(s):
                list.Add(s);
                break;
            case IEnumerable enumerable when value is not string:
                foreach (var item in enumerable)
                {
                    if (item?.ToString() is { Length: > 0 } str)
                    {
                        list.Add(str);
                    }
                }

                break;
        }

        return list;
    }

    private static CompressionLevel ParseLevel(string? level) => level?.ToLowerInvariant() switch
    {
        "fastest" => CompressionLevel.Fastest,
        "smallestsize" => CompressionLevel.SmallestSize,
        "nocompression" => CompressionLevel.NoCompression,
        _ => CompressionLevel.Optimal,
    };

    private static void WriteZip(string outputPath, List<(string Full, string Entry)> files, CompressionLevel level)
    {
        if (System.IO.File.Exists(outputPath))
        {
            System.IO.File.Delete(outputPath);
        }

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        foreach (var (full, entry) in files)
        {
            archive.CreateEntryFromFile(full, entry, level);
        }
    }

    private static async Task WriteGZipAsync(string outputPath, string source, CompressionLevel level, CancellationToken ct)
    {
        await using var input = System.IO.File.OpenRead(source);
        await using var output = System.IO.File.Create(outputPath);
        await using var gzip = new GZipStream(output, level);
        await input.CopyToAsync(gzip, ct).ConfigureAwait(false);
    }

    private static async Task WriteTarAsync(string outputPath, List<(string Full, string Entry)> files, bool gzip, CancellationToken ct)
    {
        await using var output = System.IO.File.Create(outputPath);
        Stream tarTarget = output;
        GZipStream? gzipStream = null;
        if (gzip)
        {
            gzipStream = new GZipStream(output, CompressionMode.Compress);
            tarTarget = gzipStream;
        }

        try
        {
            await using var tar = new System.Formats.Tar.TarWriter(tarTarget, leaveOpen: true);
            foreach (var (full, entry) in files)
            {
                await tar.WriteEntryAsync(full, entry, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (gzipStream is not null)
            {
                await gzipStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
