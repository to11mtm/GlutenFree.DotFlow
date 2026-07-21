// <copyright file="JsonXmlModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.FileSystem;

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 📄🏷️ Phase 2.5.a.2 — tests for the JSON and XML read/write modules~ ✨.
/// </summary>
public sealed class JsonXmlModuleTests : FileModuleTestBase
{
    private readonly JsonReadModule jsonRead = new();
    private readonly JsonWriteModule jsonWrite = new();
    private readonly XmlReadModule xmlRead = new();
    private readonly XmlWriteModule xmlWrite = new();

    [Fact]
    public void JsonXmlModules_Metadata_AreValid()
    {
        new ModuleValidator().Validate(this.jsonRead).IsValid.Should().BeTrue();
        new ModuleValidator().Validate(this.jsonWrite).IsValid.Should().BeTrue();
        new ModuleValidator().Validate(this.xmlRead).IsValid.Should().BeTrue();
        new ModuleValidator().Validate(this.xmlWrite).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ReadJson_Object_ReturnsDictAndIsArrayFalse()
    {
        var p = this.PathIn("obj.json");
        await File.WriteAllTextAsync(p, "{\"name\":\"Ada\",\"age\":36,\"active\":true}");

        var result = await this.jsonRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeTrue();
        result.Outputs["isArray"].Should().Be(false);
        var data = (IReadOnlyDictionary<string, object?>)result.Outputs["data"]!;
        data["name"].Should().Be("Ada");
        data["age"].Should().Be(36L);
        data["active"].Should().Be(true);
    }

    [Fact]
    public async Task ReadJson_Array_IsArrayTrue()
    {
        var p = this.PathIn("arr.json");
        await File.WriteAllTextAsync(p, "[1,2,3]");

        var result = await this.jsonRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Outputs["isArray"].Should().Be(true);
    }

    [Fact]
    public async Task ReadJson_Invalid_Fails()
    {
        var p = this.PathIn("bad.json");
        await File.WriteAllTextAsync(p, "{ not valid");

        var result = await this.jsonRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task WriteJson_RoundTripsNestedStructure()
    {
        var p = this.PathIn("nested.json");
        var data = new Dictionary<string, object?>
        {
            ["name"] = "Ada",
            ["tags"] = new List<object?> { "a", "b" },
            ["meta"] = new Dictionary<string, object?> { ["k"] = "v" },
        };

        var wr = await this.jsonWrite.ExecuteAsync(this.Context(
            new() { ["path"] = p, ["indented"] = true },
            new() { ["data"] = data }));
        wr.Success.Should().BeTrue();

        var rd = await this.jsonRead.ExecuteAsync(this.Context(new() { ["path"] = p }));
        var back = (IReadOnlyDictionary<string, object?>)rd.Outputs["data"]!;
        back["name"].Should().Be("Ada");
        ((IReadOnlyDictionary<string, object?>)back["meta"]!)["k"].Should().Be("v");
    }

    [Fact]
    public async Task ReadXml_ElementsAttributesText_MatchConvention()
    {
        var p = this.PathIn("doc.xml");
        await File.WriteAllTextAsync(p, "<person id=\"7\"><name>Ada</name></person>");

        var result = await this.xmlRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeTrue();
        result.Outputs["rootElement"].Should().Be("person");
        var data = (IReadOnlyDictionary<string, object?>)result.Outputs["data"]!;
        data["@id"].Should().Be("7");
        data["name"].Should().Be("Ada");
    }

    [Fact]
    public async Task ReadXml_RepeatedElements_BecomeList()
    {
        var p = this.PathIn("list.xml");
        await File.WriteAllTextAsync(p, "<items><item>a</item><item>b</item></items>");

        var result = await this.xmlRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        var data = (IReadOnlyDictionary<string, object?>)result.Outputs["data"]!;
        data["item"].Should().BeAssignableTo<System.Collections.IEnumerable>();
    }

    [Fact]
    public async Task ReadXml_ExternalEntity_Refused()
    {
        var p = this.PathIn("xxe.xml");
        await File.WriteAllTextAsync(
            p,
            "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><foo>&xxe;</foo>");

        var result = await this.xmlRead.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeFalse("DTD processing must be prohibited to prevent XXE~ 🛡️");
    }

    [Fact]
    public async Task WriteXml_RoundTripsConvention()
    {
        var p = this.PathIn("out.xml");
        var data = new Dictionary<string, object?>
        {
            ["@id"] = "7",
            ["name"] = "Ada",
        };

        var wr = await this.xmlWrite.ExecuteAsync(this.Context(
            new() { ["path"] = p, ["rootElement"] = "person" },
            new() { ["data"] = data }));
        wr.Success.Should().BeTrue();

        var rd = await this.xmlRead.ExecuteAsync(this.Context(new() { ["path"] = p }));
        var back = (IReadOnlyDictionary<string, object?>)rd.Outputs["data"]!;
        back["@id"].Should().Be("7");
        back["name"].Should().Be("Ada");
    }
}
