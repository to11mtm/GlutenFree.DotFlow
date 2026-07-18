// <copyright file="CompressionModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.FileSystem;

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🗜️📦 Phase 2.5.a.4 — tests for <see cref="CompressModule"/> and <see cref="DecompressModule"/>~ ✨.
/// </summary>
public sealed class CompressionModuleTests : FileModuleTestBase
{
    private readonly CompressModule compress = new();
    private readonly DecompressModule decompress = new();

    [Fact]
    public void CompressionModules_Metadata_AreValid()
    {
        this.compress.ModuleId.Should().Be("builtin.file.compress");
        this.decompress.ModuleId.Should().Be("builtin.file.decompress");
        new ModuleValidator().Validate(this.compress).IsValid.Should().BeTrue();
        new ModuleValidator().Validate(this.decompress).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Zip_RoundTrip_MultipleFiles()
    {
        var f1 = this.PathIn("a.txt");
        var f2 = this.PathIn("b.txt");
        await File.WriteAllTextAsync(f1, "alpha");
        await File.WriteAllTextAsync(f2, "beta");

        var zip = this.PathIn("out.zip");
        var cr = await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = new List<string> { f1, f2 },
            ["outputPath"] = zip,
            ["format"] = "zip",
        }));

        cr.Success.Should().BeTrue();
        cr.Outputs["fileCount"].Should().Be(2);
        File.Exists(zip).Should().BeTrue();

        var outDir = this.PathIn("extracted");
        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = zip,
            ["outputDirectory"] = outDir,
        }));

        dr.Success.Should().BeTrue();
        dr.Outputs["fileCount"].Should().Be(2);
        (await File.ReadAllTextAsync(Path.Combine(outDir, "a.txt"))).Should().Be("alpha");
    }

    [Fact]
    public async Task Zip_PreservesDirectoryStructure()
    {
        var subDir = this.PathIn("src");
        Directory.CreateDirectory(Path.Combine(subDir, "nested"));
        await File.WriteAllTextAsync(Path.Combine(subDir, "top.txt"), "t");
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested", "deep.txt"), "d");

        var zip = this.PathIn("dir.zip");
        await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = subDir,
            ["outputPath"] = zip,
            ["format"] = "zip",
        }));

        var outDir = this.PathIn("out");
        await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = zip,
            ["outputDirectory"] = outDir,
        }));

        File.Exists(Path.Combine(outDir, "nested", "deep.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task GZip_SingleFile_RoundTrips()
    {
        var f = this.PathIn("data.txt");
        await File.WriteAllTextAsync(f, "gzip me please~ 🗜️");

        var gz = this.PathIn("data.txt.gz");
        var cr = await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = f,
            ["outputPath"] = gz,
            ["format"] = "gzip",
        }));
        cr.Success.Should().BeTrue();

        var outDir = this.PathIn("gzout");
        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = gz,
            ["outputDirectory"] = outDir,
            ["format"] = "gzip",
        }));

        dr.Success.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(outDir, "data.txt"))).Should().Be("gzip me please~ 🗜️");
    }

    [Fact]
    public async Task GZip_MultipleSources_FailsValidation()
    {
        var f1 = this.PathIn("a.txt");
        var f2 = this.PathIn("b.txt");
        await File.WriteAllTextAsync(f1, "a");
        await File.WriteAllTextAsync(f2, "b");

        var result = await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = new List<string> { f1, f2 },
            ["outputPath"] = this.PathIn("x.gz"),
            ["format"] = "gzip",
        }));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task TarGz_RoundTrips()
    {
        var f = this.PathIn("t.txt");
        await File.WriteAllTextAsync(f, "tarred");

        var archive = this.PathIn("out.tar.gz");
        var cr = await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = f,
            ["outputPath"] = archive,
            ["format"] = "targz",
        }));
        cr.Success.Should().BeTrue();

        var outDir = this.PathIn("tgzout");
        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = archive,
            ["outputDirectory"] = outDir,
        }));

        dr.Success.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(outDir, "t.txt"))).Should().Be("tarred");
    }

    [Fact]
    public async Task Decompress_FormatInferred_FromExtension()
    {
        var f = this.PathIn("q.txt");
        await File.WriteAllTextAsync(f, "infer");
        var zip = this.PathIn("infer.zip");
        await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = f,
            ["outputPath"] = zip,
            ["format"] = "zip",
        }));

        // No explicit format — inferred from .zip~ 🔍
        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = zip,
            ["outputDirectory"] = this.PathIn("inferout"),
        }));

        dr.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Decompress_ZipSlipEntry_FailsWholeExtraction_NothingWritten()
    {
        // Craft a malicious zip with an entry that escapes the output dir~ 🛡️
        var evilZip = this.PathIn("evil.zip");
        using (var archive = ZipFile.Open(evilZip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("pwned");
        }

        var outDir = this.PathIn("safeout");
        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = evilZip,
            ["outputDirectory"] = outDir,
        }));

        dr.Success.Should().BeFalse();
        File.Exists(Path.Combine(this.TempDir, "escaped.txt")).Should().BeFalse("zip-slip must be blocked~ 🛡️");
    }

    [Fact]
    public async Task Decompress_ExistingFile_NoOverwrite_Fails()
    {
        var f = this.PathIn("dup.txt");
        await File.WriteAllTextAsync(f, "v1");
        var zip = this.PathIn("dup.zip");
        await this.compress.ExecuteAsync(this.Context(new()
        {
            ["sourcePath"] = f,
            ["outputPath"] = zip,
            ["format"] = "zip",
        }));

        var outDir = this.PathIn("dupout");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "dup.txt"), "existing");

        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = zip,
            ["outputDirectory"] = outDir,
            ["overwrite"] = false,
        }));

        dr.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Decompress_CorruptArchive_FriendlyFail()
    {
        var bad = this.PathIn("bad.zip");
        await File.WriteAllBytesAsync(bad, new byte[] { 0, 1, 2, 3, 4 });

        var dr = await this.decompress.ExecuteAsync(this.Context(new()
        {
            ["archivePath"] = bad,
            ["outputDirectory"] = this.PathIn("badout"),
        }));

        dr.Success.Should().BeFalse();
    }
}
