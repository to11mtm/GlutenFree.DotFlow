// <copyright file="FileReadWriteModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.FileSystem;

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 📖✍️ Phase 2.5.a.1 — tests for <see cref="FileReadModule"/> and <see cref="FileWriteModule"/>~ 📁✨.
/// </summary>
public sealed class FileReadWriteModuleTests : FileModuleTestBase
{
    private readonly FileReadModule read = new();
    private readonly FileWriteModule write = new();

    [Fact]
    public void ReadModule_Metadata_IsCorrect()
    {
        this.read.ModuleId.Should().Be("builtin.file.read");
        this.read.Category.Should().Be("File System");
        new ModuleValidator().Validate(this.read).IsValid.Should().BeTrue();
    }

    [Fact]
    public void WriteModule_Metadata_IsCorrect()
    {
        this.write.ModuleId.Should().Be("builtin.file.write");
        new ModuleValidator().Validate(this.write).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ReadText_Utf8_RoundTrips()
    {
        var p = this.PathIn("hello.txt");
        await File.WriteAllTextAsync(p, "héllo wörld~ 🌸");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeTrue();
        result.Outputs["content"].Should().Be("héllo wörld~ 🌸");
        ((long)result.Outputs["size"]!).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReadText_Latin1_DecodesCorrectly()
    {
        var p = this.PathIn("latin.txt");
        await File.WriteAllBytesAsync(p, Encoding.Latin1.GetBytes("café"));

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["encoding"] = "latin1" }));

        result.Success.Should().BeTrue();
        result.Outputs["content"].Should().Be("café");
    }

    [Fact]
    public async Task ReadBinary_ReturnsBytes()
    {
        var p = this.PathIn("blob.bin");
        var bytes = new byte[] { 1, 2, 3, 4, 250 };
        await File.WriteAllBytesAsync(p, bytes);

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["readAs"] = "binary" }));

        result.Success.Should().BeTrue();
        result.Outputs["content"].Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task ReadLines_ReturnsArray()
    {
        var p = this.PathIn("lines.txt");
        await File.WriteAllTextAsync(p, "one\ntwo\nthree");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["readAs"] = "lines" }));

        result.Success.Should().BeTrue();
        result.Outputs["content"].Should().BeEquivalentTo(new[] { "one", "two", "three" });
    }

    [Fact]
    public async Task Read_FileNotFound_ReturnsFriendlyFail()
    {
        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = this.PathIn("nope.txt") }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Read_ExceedsMaxSize_FailsWithoutPartialRead()
    {
        var p = this.PathIn("big.txt");
        await File.WriteAllTextAsync(p, new string('x', 1000));

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["maxSize"] = 10L }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exceeds");
    }

    [Fact]
    public async Task WriteText_Overwrite_ReplacesContent()
    {
        var p = this.PathIn("out.txt");
        await File.WriteAllTextAsync(p, "old");

        var result = await this.write.ExecuteAsync(this.Context(new()
        {
            ["path"] = p,
            ["content"] = "new content",
        }));

        result.Success.Should().BeTrue();
        (await File.ReadAllTextAsync(p)).Should().Be("new content");
    }

    [Fact]
    public async Task WriteText_Append_Appends()
    {
        var p = this.PathIn("log.txt");
        await File.WriteAllTextAsync(p, "a");

        var result = await this.write.ExecuteAsync(this.Context(new()
        {
            ["path"] = p,
            ["content"] = "b",
            ["mode"] = "append",
        }));

        result.Success.Should().BeTrue();
        (await File.ReadAllTextAsync(p)).Should().Be("ab");
    }

    [Fact]
    public async Task Write_CreateNew_ExistingFile_Fails()
    {
        var p = this.PathIn("exists.txt");
        await File.WriteAllTextAsync(p, "x");

        var result = await this.write.ExecuteAsync(this.Context(new()
        {
            ["path"] = p,
            ["content"] = "y",
            ["mode"] = "createNew",
        }));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Write_CreatesMissingDirectory_WhenEnabled()
    {
        var p = this.PathIn(Path.Combine("nested", "deep", "file.txt"));

        var result = await this.write.ExecuteAsync(this.Context(new()
        {
            ["path"] = p,
            ["content"] = "hi",
            ["createDirectory"] = true,
        }));

        result.Success.Should().BeTrue();
        File.Exists(p).Should().BeTrue();
    }

    [Fact]
    public async Task Write_BlockedExtension_Fails()
    {
        var p = this.PathIn("payload.exe");

        var result = await this.write.ExecuteAsync(this.Context(new()
        {
            ["path"] = p,
            ["content"] = "MZ",
        }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public async Task Write_BinaryContentFromPort_Wins()
    {
        var p = this.PathIn("frombytes.bin");
        var bytes = new byte[] { 9, 8, 7 };

        var result = await this.write.ExecuteAsync(this.Context(
            new() { ["path"] = p, ["content"] = "ignored" },
            new() { ["content"] = bytes }));

        result.Success.Should().BeTrue();
        (await File.ReadAllBytesAsync(p)).Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var p = this.PathIn("rt.txt");
        await this.write.ExecuteAsync(this.Context(new() { ["path"] = p, ["content"] = "round" }));

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Outputs["content"].Should().Be("round");
    }
}
