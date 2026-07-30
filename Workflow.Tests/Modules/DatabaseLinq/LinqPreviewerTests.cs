// <copyright file="LinqPreviewerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System.Collections.Generic;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Compilation;
using Workflow.Modules.Database.Linq.Preview;
using Xunit;

/// <summary>
/// 🔎 Phase 2.4.b.4 — Tests for the rollback-only in-memory SQLite previewer~ ✨💖.
/// </summary>
public sealed class LinqPreviewerTests
{
    private readonly WorkflowLinqPreviewer previewer = new(new WorkflowLinqCompiler(new TableTypeResolver()));

    [Fact]
    public async Task Preview_ReturnsSampleRowsAndDuration()
    {
        var result = await this.Preview("return db.Orders.ToList();");

        result.Success.Should().BeTrue(Dump(result));
        result.RowCount.Should().Be(3);
        result.Rows.Should().HaveCount(3);
        result.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Preview_SeedsSampleRowsPerSelectedTable()
    {
        var result = await this.Preview("return db.Orders.ToList();", sampleRows: 5);

        result.Success.Should().BeTrue(Dump(result));
        result.SampleRowsSeeded.Should().Be(5);
        result.Rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task Preview_MutationAttempt_AlwaysRolledBack()
    {
        // Inside the txn the delete is visible (0 rows), but the always-rollback wrapper restores the seed~
        var result = await this.Preview("db.Orders.Delete(); return db.Orders.ToList();");

        result.Success.Should().BeTrue(Dump(result));
        result.RowCount.Should().Be(0, "the delete is visible inside the transaction~");
        result.PostRollbackRowCount.Should().Be(3, "the delete was rolled back — seed rows survive~ 🔒");
    }

    [Fact]
    public async Task Preview_MutationsIsolatedAcrossRuns()
    {
        // Each preview spins up a fresh :memory: DB + reseeds, so a destructive body never leaks state~
        var first = await this.Preview("db.Orders.Delete(); return db.Orders.ToList();");
        var second = await this.Preview("db.Orders.Delete(); return db.Orders.ToList();");

        first.PostRollbackRowCount.Should().Be(3);
        second.PostRollbackRowCount.Should().Be(3, "the second preview starts from a fresh seed, unaffected by the first~ 🌸");
    }

    [Fact]
    public async Task Preview_CompileError_ReturnsDiagnosticsNotException()
    {
        var result = await this.Preview("return db.Ordrs.ToList();"); // typo → CS1061

        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id == "CS1061");
    }

    // ── SQL preview (L9b) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_CapturesTheExecutedSql()
    {
        var result = await this.Preview("return db.Orders.Where(o => o.total > 0m).ToList();");

        result.Success.Should().BeTrue(Dump(result));
        result.Statements.Should().NotBeNullOrEmpty();
        string.Join("\n", result.Statements!).Should().Contain("SELECT").And.Contain("Orders");
    }

    [Fact]
    public async Task Preview_UnmaterialisedQuery_RendersSqlInsteadOfFailing()
    {
        // No .ToList() — previously a materialisation error; now it renders the SQL with a hint~
        var result = await this.Preview("return db.Orders.Where(o => o.total > 0m);");

        result.Success.Should().BeTrue(Dump(result));
        result.Diagnostics.Should().Contain(d => d.Id == "WFLINQ021");
        string.Join("\n", result.Statements!).Should().Contain("SELECT");
    }

    [Fact]
    public async Task Preview_InsertStatement_IsCaptured()
    {
        var result = await this.Preview(
            "db.Insert(new Orders { id = 99, name = \"x\", total = 1m }); return db.Orders.ToList();");

        result.Success.Should().BeTrue(Dump(result));
        string.Join("\n", result.Statements!).Should().Contain("INSERT");
        result.PostRollbackRowCount.Should().Be(3, "the insert was rolled back~ 🔒");
    }

    // ── Identity / primary key columns (L9a) ─────────────────────────────────────────────

    [Fact]
    public async Task Preview_IdentityColumn_SupportsInsertWithIdentity()
    {
        var identityTable = new WorkflowTableMetadata(
            ConnectionId: "conn",
            TableName: "Items",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false, IsPrimaryKey: true, IsIdentity: true),
                new WorkflowColumnMetadata("name", "text", true),
            });

        var schema = new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        var compile = new LinqCompileRequest(
            "def1",
            "node1",
            "var id = db.InsertWithInt32Identity(new Items { name = \"new\" }); return db.Items.Where(i => i.id == id).ToList();",
            new[] { identityTable },
            schema);

        var result = await this.previewer.PreviewAsync(new LinqPreviewRequest(compile, SampleRowsPerTable: 2));

        result.Success.Should().BeTrue(Dump(result));
        result.RowCount.Should().Be(1, "the identity-generated row is readable inside the transaction~");
        result.PostRollbackRowCount.Should().Be(2, "…and rolled back afterwards~ 🔒");
    }

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: "conn",
            TableName: "Orders",
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("name", "text", true),
                new WorkflowColumnMetadata("total", "numeric", false),
            });

    private static string Dump(LinqPreviewResult r) =>
        "diagnostics: " + string.Join(" | ", System.Linq.Enumerable.Select(r.Diagnostics, d => d.Id + ":" + d.Message));

    private Task<LinqPreviewResult> Preview(string body, int sampleRows = 3)
    {
        var schema = new ModuleSchema(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        var compile = new LinqCompileRequest("def1", "node1", body, new[] { OrdersTable() }, schema);
        return this.previewer.PreviewAsync(new LinqPreviewRequest(compile, SampleRowsPerTable: sampleRows));
    }
}

