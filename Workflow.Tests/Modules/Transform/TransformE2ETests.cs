// <copyright file="TransformE2ETests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.Transform;
using Xunit;

/// <summary>
/// 📖 Phase 2.6.a.6 — end-to-end pipeline chaining the transform + file families~ 🔄✨.
/// </summary>
public sealed class TransformE2ETests : TransformModuleTestBase
{
    [Fact]
    public async Task E2E_CsvValidateMapJoinQueryAggregate_ComposesEndToEnd()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "wf-tf-e2e-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1) csv.read — people with dept + age~ 📊
            var csvPath = Path.Combine(tempDir, "people.csv");
            await File.WriteAllTextAsync(csvPath, "name,dept,age\nAda,eng,36\nGrace,eng,45\nKay,sales,28\nBad,,x\n");
            var csv = await new CsvReadModule().ExecuteAsync(this.Context(new() { ["path"] = csvPath }));
            csv.Success.Should().BeTrue();

            // 2) validate — split rows missing dept or non-numeric age~ ✅
            var validated = await new ValidateDataModule().ExecuteAsync(this.Context(
                new()
                {
                    ["rules"] = new List<object?>
                    {
                        Rec(("field", "dept"), ("rule", "required")),
                        Rec(("field", "age"), ("rule", "custom"), ("value", "!isNaN(parseInt(value))")),
                    },
                },
                new() { ["data"] = csv.Outputs["rows"] }));
            var validItems = (List<object?>)validated.Outputs["validItems"]!;
            validItems.Should().HaveCount(3, "the malformed 'Bad' row is filtered out~");

            // 3) map — convert age to int, keep name+dept~ 🔄
            var mapped = await new DataMapModule().ExecuteAsync(this.Context(
                new()
                {
                    ["mapping"] = new Dictionary<string, object?>
                    {
                        ["name"] = "name",
                        ["dept"] = "dept",
                        ["age"] = new Dictionary<string, object?> { ["path"] = "age", ["convert"] = "int" },
                    },
                },
                new() { ["source"] = validItems }));
            var mappedRows = (List<object?>)mapped.Outputs["result"]!;

            // 4) join — enrich with dept location from a reference collection~ 🔗
            var depts = new List<object?>
            {
                Rec(("dept", "eng"), ("location", "Building A")),
                Rec(("dept", "sales"), ("location", "Building B")),
            };
            var joined = await new DataJoinModule().ExecuteAsync(this.Context(
                new()
                {
                    ["leftKey"] = "dept",
                    ["rightKey"] = "dept",
                    ["joinType"] = "left",
                    ["select"] = new Dictionary<string, object?>
                    {
                        ["name"] = "left.name",
                        ["dept"] = "left.dept",
                        ["age"] = "left.age",
                        ["location"] = "right.location",
                    },
                },
                new() { ["left"] = mappedRows, ["right"] = depts }));
            var joinedRows = (List<object?>)joined.Outputs["result"]!;
            joinedRows.Should().HaveCount(3);

            // 5) query — engineers over 40, sorted by age desc~ 🔍
            var queried = await new DataQueryModule().ExecuteAsync(this.Context(
                new() { ["where"] = "item.dept === 'eng'", ["orderBy"] = "age", ["descending"] = true },
                new() { ["data"] = joinedRows }));
            var queriedRows = (List<object?>)queried.Outputs["result"]!;
            ((IReadOnlyDictionary<string, object?>)queriedRows[0]!)["name"].Should().Be("Grace");

            // 6) aggregate — sum of ages grouped by dept~ 📊
            var agg = await new AggregateModule().ExecuteAsync(this.Context(
                new() { ["operation"] = "sum", ["property"] = "age", ["groupBy"] = "dept" },
                new() { ["data"] = joinedRows }));
            var groups = (List<object?>)agg.Outputs["groups"]!;
            var eng = groups.Cast<IReadOnlyDictionary<string, object?>>().First(g => (string)g["key"]! == "eng");
            ((double)eng["result"]!).Should().Be(81);

            // 7) json.write — persist the aggregated result~ 💾
            var jsonPath = Path.Combine(tempDir, "summary.json");
            var written = await new JsonWriteModule().ExecuteAsync(this.Context(
                new() { ["path"] = jsonPath },
                new() { ["data"] = groups }));
            written.Success.Should().BeTrue();
            File.Exists(jsonPath).Should().BeTrue();
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
