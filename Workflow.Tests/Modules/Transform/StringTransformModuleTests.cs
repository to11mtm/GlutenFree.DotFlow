// <copyright file="StringTransformModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 📝 Phase 2.6.a.5 — tests for <see cref="StringTransformModule"/>~ ✨.
/// </summary>
public sealed class StringTransformModuleTests : TransformModuleTestBase
{
    private readonly StringTransformModule module = new();

    private async Task<object?> Run(string operation, object? input, Dictionary<string, object?>? parameters = null)
    {
        var props = new Dictionary<string, object?> { ["operation"] = operation };
        if (parameters is not null)
        {
            props["parameters"] = parameters;
        }

        var result = await this.module.ExecuteAsync(this.Context(props, new() { ["input"] = input }));
        result.Success.Should().BeTrue();
        return result.Outputs["result"];
    }

    [Fact]
    public void StringModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.transform.string");
        new ModuleValidator().Validate(this.module).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("upper", "hello", "HELLO")]
    [InlineData("lower", "HELLO", "hello")]
    [InlineData("trim", "  hi  ", "hi")]
    public async Task CaseAndTrim_Ops(string op, string input, string expected)
    {
        (await this.Run(op, input)).Should().Be(expected);
    }

    [Fact]
    public async Task Substring_And_Replace()
    {
        (await this.Run("substring", "hello world", new() { ["start"] = 6L })).Should().Be("world");
        (await this.Run("replace", "a-b-c", new() { ["find"] = "-", ["with"] = "_" })).Should().Be("a_b_c");
    }

    [Fact]
    public async Task Split_ReturnsArray_Join_FromArray()
    {
        (await this.Run("split", "a,b,c", new() { ["separator"] = "," })).Should().BeEquivalentTo(new object?[] { "a", "b", "c" });
        (await this.Run("join", new List<object?> { "a", "b" }, new() { ["separator"] = "-" })).Should().Be("a-b");
    }

    [Fact]
    public async Task Pad_Truncate_Format()
    {
        (await this.Run("padleft", "5", new() { ["width"] = 3L, ["char"] = "0" })).Should().Be("005");
        (await this.Run("truncate", "hello world", new() { ["length"] = 8L, ["ellipsis"] = "..." })).Should().Be("hello...");
        (await this.Run("format", "Ada", new() { ["template"] = "Hi {0}!" })).Should().Be("Hi Ada!");
    }

    [Fact]
    public async Task Regex_MatchReplaceExtract()
    {
        (await this.Run("regexmatch", "abc123", new() { ["pattern"] = @"\d+" })).Should().Be(true);
        (await this.Run("regexreplace", "a1b2", new() { ["pattern"] = @"\d", ["with"] = "#" })).Should().Be("a#b#");
        (await this.Run("regexextract", "id=42", new() { ["pattern"] = @"id=(\d+)", ["group"] = 1L })).Should().Be("42");
    }

    [Fact]
    public async Task Regex_Timeout_SafeFail()
    {
        // ReDoS pattern — must return (not hang), success or fail either way~ 🛡️
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["operation"] = "regexmatch", ["parameters"] = new Dictionary<string, object?> { ["pattern"] = "^(a+)+$" } },
            new() { ["input"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!" }));

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Base64_RoundTrips()
    {
        var encoded = (string)(await this.Run("base64encode", "hello"))!;
        (await this.Run("base64decode", encoded)).Should().Be("hello");
    }

    [Fact]
    public async Task UrlHtml_EncodeDecode_RoundTrip()
    {
        var urlEnc = (string)(await this.Run("urlencode", "a b&c"))!;
        (await this.Run("urldecode", urlEnc)).Should().Be("a b&c");

        var htmlEnc = (string)(await this.Run("htmlencode", "<b>hi</b>"))!;
        htmlEnc.Should().Contain("&lt;");
        (await this.Run("htmldecode", htmlEnc)).Should().Be("<b>hi</b>");
    }

    [Fact]
    public async Task Hash_Sha256_KnownVector()
    {
        // SHA256("abc") known digest
        (await this.Run("hash", "abc", new() { ["algorithm"] = "sha256" }))
            .Should().Be("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public async Task Hash_Md5_LegacyStillWorks()
    {
        (await this.Run("hash", "abc", new() { ["algorithm"] = "md5" }))
            .Should().Be("900150983cd24fb0d6963f7d28e17f72");
    }

    [Fact]
    public async Task NewGuid_ValidFormat()
    {
        var g = (string)(await this.Run("newguid", null))!;
        System.Guid.TryParse(g, out _).Should().BeTrue();
    }

    [Fact]
    public async Task ArrayInput_PerElement()
    {
        var result = await this.Run("upper", new List<object?> { "a", "b" });
        result.Should().BeEquivalentTo(new object?[] { "A", "B" });
    }

    [Fact]
    public void UnknownOperation_FailsValidation()
    {
        this.module.ValidateConfiguration(new Dictionary<string, object?> { ["operation"] = "explode" }).IsValid.Should().BeFalse();
    }
}
