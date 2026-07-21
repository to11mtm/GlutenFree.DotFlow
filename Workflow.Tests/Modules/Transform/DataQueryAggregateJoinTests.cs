// <copyright file="DataQueryAggregateJoinTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules.Builtin.Transform;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🔍📊🔗 Phase 2.6.a.2 — tests for query, aggregate, and join modules~ ✨.
/// </summary>
public sealed class DataQueryAggregateJoinTests : TransformModuleTestBase
{
    private readonly DataQueryModule query = new();
    private readonly AggregateModule aggregate = new();
    private readonly DataJoinModule join = new();

    private static List<object?> People() => new()
    {
        Rec(("name", "Ada"), ("age", 36L), ("dept", "eng")),
        Rec(("name", "Grace"), ("age", 45L), ("dept", "eng")),
        Rec(("name", "Kay"), ("age", 28L), ("dept", "sales")),
    };

    // ── Query ──────────────────────────────────────────────────────────────

    [Fact]
    public void Modules_Metadata_AreValid()
    {
        this.query.ModuleId.Should().Be("builtin.transform.query");
        this.aggregate.ModuleId.Should().Be("builtin.transform.aggregate");
        this.join.ModuleId.Should().Be("builtin.transform.join");
        var v = new ModuleValidator();
        v.Validate(this.query).IsValid.Should().BeTrue();
        v.Validate(this.aggregate).IsValid.Should().BeTrue();
        v.Validate(this.join).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Where_FiltersRows()
    {
        var result = await this.query.ExecuteAsync(this.Context(
            new() { ["where"] = "item.age > 30" },
            new() { ["data"] = People() }));

        result.Success.Should().BeTrue();
        result.Outputs["count"].Should().Be(2);
    }

    [Fact]
    public async Task Select_ProjectsExpression()
    {
        var result = await this.query.ExecuteAsync(this.Context(
            new() { ["select"] = "item.name" },
            new() { ["data"] = People() }));

        var list = (List<object?>)result.Outputs["result"]!;
        list.Should().BeEquivalentTo(new object?[] { "Ada", "Grace", "Kay" });
    }

    [Fact]
    public async Task Select_ProjectsMap()
    {
        var result = await this.query.ExecuteAsync(this.Context(
            new() { ["select"] = new Dictionary<string, object?> { ["n"] = "name" } },
            new() { ["data"] = People() }));

        var list = (List<object?>)result.Outputs["result"]!;
        ((IReadOnlyDictionary<string, object?>)list[0]!)["n"].Should().Be("Ada");
    }

    [Fact]
    public async Task OrderBy_Ascending_And_Descending()
    {
        var asc = await this.query.ExecuteAsync(this.Context(new() { ["orderBy"] = "age" }, new() { ["data"] = People() }));
        var ascList = (List<object?>)asc.Outputs["result"]!;
        ((IReadOnlyDictionary<string, object?>)ascList[0]!)["name"].Should().Be("Kay");

        var desc = await this.query.ExecuteAsync(this.Context(new() { ["orderBy"] = "age", ["descending"] = true }, new() { ["data"] = People() }));
        var descList = (List<object?>)desc.Outputs["result"]!;
        ((IReadOnlyDictionary<string, object?>)descList[0]!)["name"].Should().Be("Grace");
    }

    [Fact]
    public async Task SkipTake_Paginates_TotalCountPreSlice()
    {
        var result = await this.query.ExecuteAsync(this.Context(
            new() { ["orderBy"] = "age", ["skip"] = 1, ["take"] = 1 },
            new() { ["data"] = People() }));

        result.Outputs["count"].Should().Be(1);
        result.Outputs["totalCount"].Should().Be(3);
    }

    [Fact]
    public async Task CombinedPipeline_AllStages()
    {
        var result = await this.query.ExecuteAsync(this.Context(
            new()
            {
                ["where"] = "item.dept === 'eng'",
                ["orderBy"] = "age",
                ["descending"] = true,
                ["select"] = "item.name",
                ["take"] = 1,
            },
            new() { ["data"] = People() }));

        ((List<object?>)result.Outputs["result"]!)[0].Should().Be("Grace");
    }

    [Fact]
    public async Task EmptyData_ReturnsEmptyNotError()
    {
        var result = await this.query.ExecuteAsync(this.Context(new() { ["where"] = "item.x > 0" }, new() { ["data"] = new List<object?>() }));
        result.Success.Should().BeTrue();
        result.Outputs["count"].Should().Be(0);
    }

    // ── Aggregate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sum_And_Avg_OnProperty()
    {
        var sum = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "sum", ["property"] = "age" }, new() { ["data"] = People() }));
        ((double)sum.Outputs["result"]!).Should().Be(109);

        var avg = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "avg", ["property"] = "age" }, new() { ["data"] = People() }));
        ((double)avg.Outputs["result"]!).Should().BeApproximately(36.33, 0.01);
    }

    [Fact]
    public async Task MinMax_And_Count()
    {
        (await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "min", ["property"] = "age" }, new() { ["data"] = People() }))).Outputs["result"].Should().Be(28d);
        (await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "max", ["property"] = "age" }, new() { ["data"] = People() }))).Outputs["result"].Should().Be(45d);
        (await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "count" }, new() { ["data"] = People() }))).Outputs["result"].Should().Be(3);
    }

    [Fact]
    public async Task Distinct_ReturnsUniques()
    {
        var result = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "distinct", ["property"] = "dept" }, new() { ["data"] = People() }));
        ((List<object?>)result.Outputs["result"]!).Should().BeEquivalentTo(new object?[] { "eng", "sales" });
    }

    [Fact]
    public async Task Median_Works()
    {
        var result = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "median", ["property"] = "age" }, new() { ["data"] = People() }));
        ((double)result.Outputs["result"]!).Should().Be(36);
    }

    [Fact]
    public async Task GroupBy_AggregatesPerGroup()
    {
        var result = await this.aggregate.ExecuteAsync(this.Context(
            new() { ["operation"] = "sum", ["property"] = "age", ["groupBy"] = "dept" },
            new() { ["data"] = People() }));

        var groups = (List<object?>)result.Outputs["groups"]!;
        groups.Should().HaveCount(2);
        var eng = groups.Cast<IReadOnlyDictionary<string, object?>>().First(g => (string)g["key"]! == "eng");
        ((double)eng["result"]!).Should().Be(81);
        eng["count"].Should().Be(2);
    }

    [Fact]
    public async Task EmptyCollection_SumZero_OthersNull()
    {
        var sum = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "sum", ["property"] = "age" }, new() { ["data"] = new List<object?>() }));
        sum.Outputs["result"].Should().Be(0d);
        var avg = await this.aggregate.ExecuteAsync(this.Context(new() { ["operation"] = "avg", ["property"] = "age" }, new() { ["data"] = new List<object?>() }));
        avg.Outputs["result"].Should().BeNull();
    }

    [Fact]
    public void Aggregate_UnknownOperation_FailsValidation()
    {
        this.aggregate.ValidateConfiguration(new Dictionary<string, object?> { ["operation"] = "bogus" }).IsValid.Should().BeFalse();
    }

    // ── Join ───────────────────────────────────────────────────────────────

    private static List<object?> Orders() => new()
    {
        Rec(("id", 1L), ("customerId", 10L)),
        Rec(("id", 2L), ("customerId", 20L)),
        Rec(("id", 3L), ("customerId", 99L)),
    };

    private static List<object?> Customers() => new()
    {
        Rec(("cid", 10L), ("name", "Ada")),
        Rec(("cid", 20L), ("name", "Grace")),
        Rec(("cid", 30L), ("name", "Kay")),
    };

    [Fact]
    public async Task InnerJoin_MatchesOnKey()
    {
        var result = await this.join.ExecuteAsync(this.Context(
            new() { ["leftKey"] = "customerId", ["rightKey"] = "cid", ["joinType"] = "inner" },
            new() { ["left"] = Orders(), ["right"] = Customers() }));

        result.Success.Should().BeTrue();
        result.Outputs["count"].Should().Be(2);
    }

    [Fact]
    public async Task LeftJoin_UnmatchedLeft_RightNull()
    {
        var result = await this.join.ExecuteAsync(this.Context(
            new() { ["leftKey"] = "customerId", ["rightKey"] = "cid", ["joinType"] = "left" },
            new() { ["left"] = Orders(), ["right"] = Customers() }));

        result.Outputs["count"].Should().Be(3);
        result.Outputs["unmatchedLeft"].Should().Be(1);
        var rows = (List<object?>)result.Outputs["result"]!;
        var order3 = rows.Cast<IReadOnlyDictionary<string, object?>>().First(r => (long)r["id"]! == 3L);
        order3["right"].Should().BeNull();
    }

    [Fact]
    public async Task FullJoin_EmitsBothUnmatchedSides()
    {
        var result = await this.join.ExecuteAsync(this.Context(
            new() { ["leftKey"] = "customerId", ["rightKey"] = "cid", ["joinType"] = "full" },
            new() { ["left"] = Orders(), ["right"] = Customers() }));

        // 2 matched + 1 unmatched left + 1 unmatched right (cid 30)
        result.Outputs["count"].Should().Be(4);
        result.Outputs["unmatchedRight"].Should().Be(1);
    }

    [Fact]
    public async Task CustomSelect_ShapesOutput()
    {
        var result = await this.join.ExecuteAsync(this.Context(
            new()
            {
                ["leftKey"] = "customerId",
                ["rightKey"] = "cid",
                ["select"] = new Dictionary<string, object?> { ["orderId"] = "left.id", ["customer"] = "right.name" },
            },
            new() { ["left"] = Orders(), ["right"] = Customers() }));

        var rows = (List<object?>)result.Outputs["result"]!;
        var first = (IReadOnlyDictionary<string, object?>)rows[0]!;
        first.Should().ContainKey("orderId");
        first.Should().ContainKey("customer");
    }

    [Fact]
    public async Task Join_MissingKey_FailsValidation()
    {
        this.join.ValidateConfiguration(new Dictionary<string, object?> { ["rightKey"] = "cid" }).IsValid.Should().BeFalse();
    }
}
