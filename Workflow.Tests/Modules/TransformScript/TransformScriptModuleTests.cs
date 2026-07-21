// <copyright file="TransformScriptModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.TransformScript;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Transform.Script;
using Workflow.Modules.Transform.Script.Abstractions;
using Workflow.Modules.Transform.Script.Builtin;
using Workflow.Modules.Validation;
using Workflow.Persistence.Abstractions;
using Workflow.Scripting.Roslyn.Abstractions;
using Workflow.Scripting.Roslyn.Execution;
using Workflow.Tests.Scripting;
using Xunit;

/// <summary>
/// 🌟 Phase 2.6.b.1 — tests for <see cref="TransformScriptModule"/> (compile → cache → ALC execute)~ ✨.
/// </summary>
public sealed class TransformScriptModuleTests : IDisposable
{
    private readonly ServiceProvider services;
    private readonly TransformScriptModule module = new();
    private readonly ITransformScriptCompiler compiler;
    private readonly ICompiledScriptCache cache;

    public TransformScriptModuleTests()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        sc.AddSingleton<IBlobStore, InMemoryBlobStore>();
        sc.AddTransformScriptModules();
        this.services = sc.BuildServiceProvider();
        this.compiler = this.services.GetRequiredService<ITransformScriptCompiler>();
        this.cache = this.services.GetRequiredService<ICompiledScriptCache>();
    }

    public void Dispose() => this.services.Dispose();

    private ModuleExecutionContext Context(Dictionary<string, object?> props, Dictionary<string, object?>? inputs = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = this.services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "script-node",
        };

    private async Task<string> CompileAndStore(string body)
    {
        var result = this.compiler.Compile(body);
        result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics));
        var key = ScriptAssemblyKey.Build("compiled-modules/transform", "def", "node", body, this.compiler.SchemaVersion, "in");
        await this.cache.StoreAsync(key, result.AssemblyBytes!);
        return key;
    }

    private static List<object?> Rows(params (string, object?)[][] records)
    {
        var list = new List<object?>();
        foreach (var rec in records)
        {
            var d = new Dictionary<string, object?>();
            foreach (var (k, v) in rec)
            {
                d[k] = v;
            }

            list.Add(d);
        }

        return list;
    }

    [Fact]
    public void ScriptModule_Metadata_IsCorrect()
    {
        this.module.ModuleId.Should().Be("builtin.transform.script");
        this.module.Category.Should().Be("Transformation");
        new ModuleValidator().Validate(this.module).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Compile_ForbiddenApi_FileIo_Rejected()
    {
        var result = this.compiler.Compile("System.IO.File.Delete(\"x\"); return null;");
        result.Success.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Id.StartsWith("WFSCRIPT"));
    }

    [Fact]
    public async Task Execute_SimpleProjection_RoundTrips()
    {
        var key = await this.CompileAndStore(
            "return rows.Where(r => (long)r[\"age\"] > 30).Select(r => r[\"name\"]).ToList();");

        var data = Rows(
            new[] { ("name", (object?)"Ada"), ("age", (object?)36L) },
            new[] { ("name", (object?)"Kay"), ("age", (object?)28L) });

        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["compiledAssemblyKey"] = key },
            new() { ["data"] = data }));

        result.Success.Should().BeTrue();
        ((List<object?>)result.Outputs["result"]!).Should().BeEquivalentTo(new object?[] { "Ada" });
    }

    [Fact]
    public async Task Execute_LinqJoinOverRows_Works()
    {
        // Group + aggregate in a single script — the "power" case joins/regroups can't do declaratively~
        var key = await this.CompileAndStore(
            "return rows.GroupBy(r => (string)r[\"dept\"])" +
            ".Select(g => (object)new Dictionary<string, object?>{ [\"dept\"] = g.Key, [\"total\"] = g.Sum(r => (long)r[\"age\"]) })" +
            ".ToList();");

        var data = Rows(
            new[] { ("dept", (object?)"eng"), ("age", (object?)36L) },
            new[] { ("dept", (object?)"eng"), ("age", (object?)45L) },
            new[] { ("dept", (object?)"sales"), ("age", (object?)28L) });

        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["compiledAssemblyKey"] = key },
            new() { ["data"] = data }));

        result.Success.Should().BeTrue();
        result.Outputs["rowCount"].Should().Be(2);
    }

    [Fact]
    public async Task Execute_UsesInputs()
    {
        var key = await this.CompileAndStore("return rows.Count + (int)(long)inputs[\"bonus\"];");

        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["compiledAssemblyKey"] = key, ["inputs"] = new Dictionary<string, object?> { ["bonus"] = 5L } },
            new() { ["data"] = Rows(new[] { ("x", (object?)1L) }) }));

        result.Outputs["result"].Should().Be(6);
    }

    [Fact]
    public async Task Execute_MissingCompiledAssembly_Fails()
    {
        var result = await this.module.ExecuteAsync(this.Context(
            new() { ["compiledAssemblyKey"] = "compiled-modules/transform/none/none/deadbeef.dll" },
            new() { ["data"] = Rows() }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task Execute_TamperedBlob_RejectedAtLoad()
    {
        var body = "return rows.Count;";
        var result = this.compiler.Compile(body);
        var key = ScriptAssemblyKey.Build("compiled-modules/transform", "d", "n", body, this.compiler.SchemaVersion, "in");
        await this.cache.StoreAsync(key, result.AssemblyBytes!);

        var blob = (InMemoryBlobStore)this.services.GetRequiredService<IBlobStore>();
        blob.Corrupt(key);

        // Fresh cache (bypass LRU) resolved from a new provider sharing the same blob store~
        var freshCache = new CompiledScriptCache(blob, this.services.GetRequiredService<IScriptAssemblySigner>());
        (await freshCache.TryGetAsync(key)).Should().BeNull();
    }

    [Fact]
    public void Compile_ThenValidate_MissingKey_Fails()
    {
        this.module.ValidateConfiguration(new Dictionary<string, object?>()).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_MissingServices_Fails()
    {
        using var bare = new ServiceCollection().AddWorkflowModules().BuildServiceProvider();
        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?> { ["compiledAssemblyKey"] = "k" },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = bare,
            ExecutionId = Guid.NewGuid(),
            NodeId = "n",
        };

        var result = await this.module.ExecuteAsync(ctx);
        result.Success.Should().BeFalse();
    }
}
