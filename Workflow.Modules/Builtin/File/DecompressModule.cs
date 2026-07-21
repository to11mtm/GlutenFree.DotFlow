// <copyright file="DecompressModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.File;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File.Internal;

/// <summary>
/// 📦 Built-in Decompress module (<c>builtin.file.decompress</c>) — extracts a Zip / GZip / Tar /
/// TarGz archive to a directory, with zip-slip protection~ 📁✨.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.5.a.4. Every entry is validated against the output directory
/// <b>before any bytes land</b> — a hostile entry (<c>../escape</c>) fails the whole extraction~ 🛡️.
/// </remarks>
public sealed class DecompressModule : IWorkflowModule
{
    /// <inheritdoc />
    public string ModuleId => "builtin.file.decompress";

    /// <inheritdoc />
    public string DisplayName => "Decompress Files";

    /// <inheritdoc />
    public string Category => "File System";

    /// <inheritdoc />
    public string Description => "Extracts a Zip/GZip/Tar/TarGz archive~ 📦✨";

    /// <inheritdoc />
    public string Icon => "📦";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition("extractedFiles", "Extracted Files", typeof(object), "Absolute paths of extracted files~ 📄", false),
            new PortDefinition("fileCount", "File Count", typeof(int), "Number of files extracted~ 🔢", false),
            new PortDefinition("success", "Success", typeof(bool), "Whether extraction succeeded~ ✅", false)),
        Properties: Arr.create(
            new ModulePropertyDefinition("archivePath", "Archive Path", typeof(string), "Archive file path~ 📦", true, null, PropertyEditorType.FilePath),
            new ModulePropertyDefinition("outputDirectory", "Output Directory", typeof(string), "Destination directory~ 📂", true, null, PropertyEditorType.DirectoryPath),
            new ModulePropertyDefinition("format", "Format", typeof(string), "zip, gzip, tar, targz (inferred if omitted)~ 🗜️", false, null, PropertyEditorType.Dropdown),
            new ModulePropertyDefinition("overwrite", "Overwrite", typeof(bool), "Overwrite existing files~ ♻️", false, false, PropertyEditorType.Boolean)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        if (FileModuleSupport.GetString(configuration, "archivePath") is null)
        {
            errors.Add(new ValidationError("ARCHIVE_REQUIRED", "archivePath is required~ 💔", PropertyName: "archivePath"));
        }

        if (FileModuleSupport.GetString(configuration, "outputDirectory") is null)
        {
            errors.Add(new ValidationError("OUTPUT_REQUIRED", "outputDirectory is required~ 💔", PropertyName: "outputDirectory"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var rawArchive = FileModuleSupport.GetString(context.Properties, "archivePath");
        var rawOutDir = FileModuleSupport.GetString(context.Properties, "outputDirectory");
        if (rawArchive is null || rawOutDir is null)
        {
            return ModuleResult.Fail("archivePath and outputDirectory are required~ 💔");
        }

        if (!FileModuleSupport.TryValidatePath(context, rawArchive, PathAccessIntent.Read, out var archivePath, out var aFail))
        {
            return aFail!;
        }

        if (!FileModuleSupport.TryValidatePath(context, rawOutDir, PathAccessIntent.Write, out var outDir, out var oFail))
        {
            return oFail!;
        }

        if (!System.IO.File.Exists(archivePath))
        {
            return ModuleResult.Fail($"📦 Archive not found: '{rawArchive}'~ 💔");
        }

        var format = (FileModuleSupport.GetString(context.Properties, "format") ?? InferFormat(archivePath)).ToLowerInvariant();
        var overwrite = FileModuleSupport.GetBool(context.Properties, "overwrite", false);

        var sw = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(outDir);
            var fullOutDir = Path.GetFullPath(outDir);

            var extracted = format switch
            {
                "zip" => ExtractZip(archivePath, fullOutDir, overwrite),
                "gzip" => await ExtractGZipAsync(archivePath, fullOutDir, overwrite, cancellationToken).ConfigureAwait(false),
                "tar" => await ExtractTarAsync(archivePath, fullOutDir, overwrite, gzip: false, cancellationToken).ConfigureAwait(false),
                "targz" => await ExtractTarAsync(archivePath, fullOutDir, overwrite, gzip: true, cancellationToken).ConfigureAwait(false),
                _ => null,
            };

            if (extracted is null)
            {
                return ModuleResult.Fail($"📦 Unknown archive format '{format}'~ 💔");
            }

            sw.Stop();

            var outputs = new Dictionary<string, object?>
            {
                ["extractedFiles"] = extracted,
                ["fileCount"] = extracted.Count,
                ["success"] = true,
            };

            context.Logger.LogDebug("📦 Extracted {Count} files from {Path}", extracted.Count, archivePath);
            return ModuleResult.Ok(outputs, ExecutionMetrics.FromDuration(sw.Elapsed));
        }
        catch (PathSecurityException ex)
        {
            return ModuleResult.Fail($"🛡️ {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ModuleResult.Fail($"📦 Extraction failed: {ex.Message}~ 💔", ex);
        }
    }

    private static string InferFormat(string archivePath)
    {
        var lower = archivePath.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
        {
            return "targz";
        }

        if (lower.EndsWith(".tar"))
        {
            return "tar";
        }

        if (lower.EndsWith(".gz"))
        {
            return "gzip";
        }

        return "zip";
    }

    private static string SafeTargetPath(string outDir, string entryName)
    {
        // Reject absolute/rooted entries and normalise separators~ 🛡️
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        var target = Path.GetFullPath(Path.Combine(outDir, normalized));

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var rootWithSep = outDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootWithSep, comparison) &&
            !string.Equals(target, outDir.TrimEnd(Path.DirectorySeparatorChar), comparison))
        {
            throw new PathSecurityException(entryName, "archive entry escapes the output directory (zip-slip)");
        }

        return target;
    }

    private static List<string> ExtractZip(string archivePath, string outDir, bool overwrite)
    {
        using var archive = ZipFile.OpenRead(archivePath);

        // 🛡️ Pre-scan: validate every entry before writing anything
        var plan = new List<(ZipArchiveEntry Entry, string Target)>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue; // directory entry
            }

            var target = SafeTargetPath(outDir, entry.FullName);
            if (!overwrite && System.IO.File.Exists(target))
            {
                throw new IOException($"file already exists: '{target}' (set overwrite=true)");
            }

            plan.Add((entry, target));
        }

        var extracted = new List<string>();
        foreach (var (entry, target) in plan)
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            entry.ExtractToFile(target, overwrite);
            extracted.Add(target);
        }

        return extracted;
    }

    private static async Task<List<string>> ExtractGZipAsync(string archivePath, string outDir, bool overwrite, CancellationToken ct)
    {
        // GZip holds a single stream; derive the output name from the archive name~ 📦
        var name = Path.GetFileNameWithoutExtension(archivePath);
        var target = SafeTargetPath(outDir, name);
        if (!overwrite && System.IO.File.Exists(target))
        {
            throw new IOException($"file already exists: '{target}' (set overwrite=true)");
        }

        Directory.CreateDirectory(outDir);
        await using (var input = System.IO.File.OpenRead(archivePath))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = System.IO.File.Create(target))
        {
            await gzip.CopyToAsync(output, ct).ConfigureAwait(false);
        }

        return new List<string> { target };
    }

    private static async Task<List<string>> ExtractTarAsync(string archivePath, string outDir, bool overwrite, bool gzip, CancellationToken ct)
    {
        await using var input = System.IO.File.OpenRead(archivePath);
        Stream source = input;
        GZipStream? gzipStream = null;
        if (gzip)
        {
            gzipStream = new GZipStream(input, CompressionMode.Decompress);
            source = gzipStream;
        }

        var extracted = new List<string>();
        try
        {
            await using var tar = new System.Formats.Tar.TarReader(source, leaveOpen: true);
            while (await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
            {
                if (entry.EntryType is System.Formats.Tar.TarEntryType.Directory)
                {
                    continue;
                }

                var target = SafeTargetPath(outDir, entry.Name);
                if (!overwrite && System.IO.File.Exists(target))
                {
                    throw new IOException($"file already exists: '{target}' (set overwrite=true)");
                }

                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await entry.ExtractToFileAsync(target, overwrite, ct).ConfigureAwait(false);
                extracted.Add(target);
            }
        }
        finally
        {
            if (gzipStream is not null)
            {
                await gzipStream.DisposeAsync().ConfigureAwait(false);
            }
        }

        return extracted;
    }
}
