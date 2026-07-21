// <copyright file="LinqCompilerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.DatabaseLinq;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Linq.Abstractions;
using Workflow.Modules.Database.Linq.Compilation;
using Xunit;

/// <summary>
/// 🧬🔴 Phase 2.4.b.1 — Tests for the Roslyn linq compiler: codegen, dual-POCO resolution,
/// typed-input validation, and the security allowlist~ ✨💖.
/// </summary>
public sealed class LinqCompilerTests
{
    private readonly WorkflowLinqCompiler compiler = new(new TableTypeResolver());

    // ── Column-generated POCO path ───────────────────────────────────────────────────────

    [Fact]
    public async Task Compile_ValidQuery_Succeeds()
    {
        var result = await this.Compile(
            "return db.Orders.Where(o => o.total > 0m).ToList();",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeTrue(this.Dump(result));
        result.AssemblyBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Compile_ColumnGeneratedPocoTable_Succeeds()
    {
        // Uses a generated column property (customer_id) — proves the generated-POCO path works end to end~
        var result = await this.Compile(
            "return db.Orders.Select(o => o.customer_id).ToList();",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeTrue(this.Dump(result));
    }

    [Fact]
    public async Task Compile_TypoInTableName_ReturnsMemberDiagnostic()
    {
        var result = await this.Compile(
            "return db.Ordrs.ToList();",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "CS1061" && d.Message.Contains("Ordrs", StringComparison.Ordinal));
    }

    // ── Typed-input validation (LinqInputs) ──────────────────────────────────────────────

    [Fact]
    public async Task Compile_TypoInInputProperty_ReturnsCS1061Diagnostic()
    {
        var result = await this.Compile(
            "return db.Orders.Where(o => o.total > inputs.MinTottal).ToList();",
            new[] { OrdersTable() },
            Schema(Prop("MinTotal", typeof(decimal))));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "CS1061");
    }

    [Fact]
    public async Task Compile_WrongTypeComparison_ReturnsCS0019Diagnostic()
    {
        var result = await this.Compile(
            "return db.Orders.Where(o => o.total > inputs.Name).ToList();",
            new[] { OrdersTable() },
            Schema(Prop("Name", typeof(string))));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "CS0019");
    }

    [Fact]
    public async Task LinqInputs_Codegen_AllowlistedScalarTypes_EmitTypedProperties()
    {
        var result = await this.Compile(
            "return db.Orders.Where(o => o.customer_id == inputs.CustomerId && o.total >= inputs.MinTotal).ToList();",
            new[] { OrdersTable() },
            Schema(Prop("CustomerId", typeof(Guid), required: true), Prop("MinTotal", typeof(decimal))));

        result.Success.Should().BeTrue(this.Dump(result));
    }

    [Fact]
    public async Task LinqInputs_Codegen_NonAllowlistedType_FallsBackToObjectWithWarning()
    {
        var result = await this.Compile(
            "return db.Orders.ToList();",
            new[] { OrdersTable() },
            Schema(Prop("Builder", typeof(System.Text.StringBuilder))));

        result.Success.Should().BeTrue(this.Dump(result));
        result.Warnings.Should().Contain(d => d.Id == "WFLINQ004");
    }

    [Fact]
    public async Task LinqInputs_Codegen_StrictMode_NonAllowlistedType_Rejected()
    {
        var result = await this.Compile(
            "return db.Orders.ToList();",
            new[] { OrdersTable() },
            Schema(Prop("Builder", typeof(System.Text.StringBuilder))),
            strict: true);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ004");
    }

    // ── Security allowlist (mitigates C1) ────────────────────────────────────────────────

    [Fact]
    public async Task Compile_ForbiddenApi_ProcessStart_Rejected()
    {
        var result = await this.Compile(
            "System.Diagnostics.Process.Start(\"calc\"); return null;",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ100");
    }

    [Fact]
    public async Task Compile_ForbiddenApi_FileIo_Rejected()
    {
        var result = await this.Compile(
            "System.IO.File.Delete(\"/etc/passwd\"); return null;",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ100");
    }

    [Fact]
    public async Task Compile_ForbiddenApi_HttpClient_Rejected()
    {
        var result = await this.Compile(
            "var c = new System.Net.Http.HttpClient(); return null;",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ100");
    }

    [Fact]
    public async Task Compile_UnsafeBlock_Rejected()
    {
        var result = await this.Compile(
            "unsafe { int x = 1; } return null;",
            new[] { OrdersTable() },
            Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ102");
    }

    // ── Plugin POCO path + resolution errors ─────────────────────────────────────────────

    [Fact]
    public async Task Compile_PluginPocoTable_Succeeds()
    {
        var table = new WorkflowTableMetadata(
            ConnectionId: "conn",
            TableName: "Orders",
            Schema: null,
            Columns: null,
            ClrTypeName: typeof(PluginOrder).FullName,
            AssemblyName: typeof(PluginOrder).Assembly.GetName().Name);

        var result = await this.Compile(
            "return db.Orders.Where(o => o.Id > 0 && o.Total > 10m).ToList();",
            new[] { table },
            Schema());

        result.Success.Should().BeTrue(this.Dump(result));
    }

    [Fact]
    public async Task Compile_TableWithNoTypeOrColumns_ReturnsDiagnostic()
    {
        var table = new WorkflowTableMetadata("conn", "Orphan", Schema: null, Columns: null, ClrTypeName: null, AssemblyName: null);

        var result = await this.Compile("return db.Orphan.ToList();", new[] { table }, Schema());

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(d => d.Id == "WFLINQ001");
    }

    // ── Helpers 🛠️ ───────────────────────────────────────────────────────────────────────

    private static WorkflowTableMetadata OrdersTable() =>
        new(
            ConnectionId: "conn",
            TableName: "Orders",
            Schema: null,
            Columns: new[]
            {
                new WorkflowColumnMetadata("id", "integer", false),
                new WorkflowColumnMetadata("customer_id", "uuid", false),
                new WorkflowColumnMetadata("total", "numeric", false),
            },
            ClrTypeName: null,
            AssemblyName: null);

    private static ModulePropertyDefinition Prop(string name, Type type, bool required = false) =>
        new(name, name, type, IsRequired: required);

    private static ModuleSchema Schema(params ModulePropertyDefinition[] props) =>
        new(Arr<PortDefinition>.Empty, Arr<PortDefinition>.Empty, Arr.create(props));

    private Task<LinqCompileResult> Compile(
        string body,
        IReadOnlyList<WorkflowTableMetadata> tables,
        ModuleSchema schema,
        bool strict = false)
        => this.compiler.CompileAsync(new LinqCompileRequest("def1", "node1", body, tables, schema, strict));

#pragma warning disable CA1822 // instance for `this.Dump(...)` call-site ergonomics~
    private string Dump(LinqCompileResult r) =>
        "errors: " + string.Join(" | ", r.Errors.Select(e => $"{e.Id}:{e.Message}"));
#pragma warning restore CA1822
}

/// <summary>🧩 A top-level plugin POCO used by the plugin-path compile test~.</summary>
public sealed class PluginOrder
{
    /// <summary>Gets or sets the order id.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the order total.</summary>
    public decimal Total { get; set; }
}



