// <copyright file="CsvModuleTests.cs" company="GlutenFree">
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
/// 📊 Phase 2.5.a.2 — tests for <see cref="CsvReadModule"/> and <see cref="CsvWriteModule"/>~ ✨.
/// </summary>
public sealed class CsvModuleTests : FileModuleTestBase
{
    private readonly CsvReadModule read = new();
    private readonly CsvWriteModule write = new();

    [Fact]
    public void CsvModules_Metadata_AreValid()
    {
        this.read.ModuleId.Should().Be("builtin.file.csv.read");
        this.write.ModuleId.Should().Be("builtin.file.csv.write");
        new ModuleValidator().Validate(this.read).IsValid.Should().BeTrue();
        new ModuleValidator().Validate(this.write).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ReadCsv_WithHeader_RowsKeyedByHeader()
    {
        var p = this.PathIn("h.csv");
        await File.WriteAllTextAsync(p, "name,age\nAda,36\nGrace,45\n");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p }));

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(2);
        var rows = (List<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows[0]["name"].Should().Be("Ada");
        rows[0]["age"].Should().Be("36");
    }

    [Fact]
    public async Task ReadCsv_NoHeader_ColumnNKeys()
    {
        var p = this.PathIn("nh.csv");
        await File.WriteAllTextAsync(p, "Ada,36\nGrace,45\n");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["hasHeader"] = false }));

        var rows = (List<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().HaveCount(2);
        rows[0]["column0"].Should().Be("Ada");
        rows[0]["column1"].Should().Be("36");
    }

    [Fact]
    public async Task ReadCsv_SemicolonDelimiter()
    {
        var p = this.PathIn("semi.csv");
        await File.WriteAllTextAsync(p, "a;b\n1;2\n");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p, ["delimiter"] = ";" }));

        var rows = (List<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows[0]["a"].Should().Be("1");
        rows[0]["b"].Should().Be("2");
    }

    [Fact]
    public async Task ReadCsv_QuotedFieldWithCommaAndNewline()
    {
        var p = this.PathIn("q.csv");
        await File.WriteAllTextAsync(p, "name,note\n\"Ada\",\"hello, world\nsecond line\"\n");

        var result = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p }));

        var rows = (List<IReadOnlyDictionary<string, object?>>)result.Outputs["rows"]!;
        rows.Should().HaveCount(1);
        ((string)rows[0]["note"]!).Should().Contain("hello, world").And.Contain("second line");
    }

    [Fact]
    public async Task WriteCsv_RoundTripsThroughRead()
    {
        var p = this.PathIn("out.csv");
        var data = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Ada", ["age"] = "36" },
            new() { ["name"] = "Grace", ["age"] = "45" },
        };

        var wr = await this.write.ExecuteAsync(this.Context(
            new() { ["path"] = p },
            new() { ["data"] = data }));

        wr.Success.Should().BeTrue();
        wr.Outputs["rowsWritten"].Should().Be(2);

        var rd = await this.read.ExecuteAsync(this.Context(new() { ["path"] = p }));
        var rows = (List<IReadOnlyDictionary<string, object?>>)rd.Outputs["rows"]!;
        rows[1]["name"].Should().Be("Grace");
    }

    [Fact]
    public async Task WriteCsv_EmptyData_HeaderlessEmptyFile()
    {
        var p = this.PathIn("empty.csv");

        var wr = await this.write.ExecuteAsync(this.Context(
            new() { ["path"] = p },
            new() { ["data"] = new List<Dictionary<string, object?>>() }));

        wr.Success.Should().BeTrue();
        wr.Outputs["rowsWritten"].Should().Be(0);
        File.Exists(p).Should().BeTrue();
    }
}
