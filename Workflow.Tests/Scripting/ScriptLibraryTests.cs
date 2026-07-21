// <copyright file="ScriptLibraryTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Scripting.Executors;
using Workflow.Scripting.Libraries;
using Xunit;

/// <summary>
/// 📚 Phase 3.1.5 — Tests for the script library store, resolution, and import mechanics~ ✨.
/// </summary>
public sealed class ScriptLibraryTests
{
    [Fact]
    public async Task Store_SaveGetDelete_RoundTrips()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "math", Language = "javascript", Code = "return {}" });

        (await store.GetAsync("math")).Should().NotBeNull();
        (await store.DeleteAsync("math")).Should().BeTrue();
        (await store.GetAsync("math")).Should().BeNull();
    }

    [Fact]
    public async Task Store_ListByLanguage_Filters()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "a", Language = "javascript", Code = "x" });
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "b", Language = "lua", Code = "y" });

        var js = await store.GetAllAsync("javascript");
        js.Should().ContainSingle().Which.LibraryId.Should().Be("a");
    }

    [Fact]
    public async Task Store_DependencyCycle_Rejected()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "a", Language = "javascript", Code = "1", Dependencies = new[] { "b" } });
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "b", Language = "javascript", Code = "2", Dependencies = new[] { "a" } });

        var act = async () => await store.ResolveAsync("javascript", new[] { "a" });
        await act.Should().ThrowAsync<ScriptLibraryException>().WithMessage("*Circular*");
    }

    [Fact]
    public async Task Resolve_MissingLibrary_ClearError()
    {
        var store = new InMemoryScriptLibraryStore();
        var act = async () => await store.ResolveAsync("javascript", new[] { "ghost" });
        await act.Should().ThrowAsync<ScriptLibraryException>().WithMessage("*Unknown library*");
    }

    [Fact]
    public async Task Resolve_WrongLanguage_ClearError()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "lualib", Language = "lua", Code = "x" });

        var act = async () => await store.ResolveAsync("javascript", new[] { "lualib" });
        await act.Should().ThrowAsync<ScriptLibraryException>().WithMessage("*cannot be imported*");
    }

    [Fact]
    public async Task Resolve_Dependencies_LoadInOrder()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "base", Language = "javascript", Code = "b" });
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "top", Language = "javascript", Code = "t", Dependencies = new[] { "base" } });

        var sources = await store.ResolveAsync("javascript", new[] { "top" });

        sources.Select(s => s.LibraryId).Should().ContainInOrder("base", "top");
    }

    [Fact]
    public async Task Js_Require_CallsLibraryFunction()
    {
        var store = new InMemoryScriptLibraryStore();
        await store.SaveAsync(new ScriptLibraryDefinition
        {
            LibraryId = "mathlib",
            Language = "javascript",
            Code = "return { add: function(a, b) { return a + b; } };",
        });

        var libraries = await store.ResolveAsync("javascript", new[] { "mathlib" });
        var (context, _) = ScriptTestHarness.BuildContext(libraries: libraries);

        var executor = new JavaScriptScriptExecutor();
        var result = await executor.ExecuteAsync("var m = workflow.require('mathlib'); return m.add(2, 5);", context);

        result.Success.Should().BeTrue(result.Error);
        result.ReturnValue.Should().Be(7);
    }

    [Fact]
    public async Task PersistedStore_RoundTrips()
    {
        var persistence = new InMemoryLibraryPersistence();
        var store = new PersistedScriptLibraryStore(persistence);
        await store.SaveAsync(new ScriptLibraryDefinition { LibraryId = "p", Language = "lua", Code = "z" });

        var reloaded = new PersistedScriptLibraryStore(persistence);
        (await reloaded.GetAsync("p")).Should().NotBeNull();
    }

    private sealed class InMemoryLibraryPersistence : IScriptLibraryPersistence
    {
        private string? json;

        public Task<string?> ReadAsync(CancellationToken ct = default) => Task.FromResult(this.json);

        public Task WriteAsync(string json, CancellationToken ct = default)
        {
            this.json = json;
            return Task.CompletedTask;
        }
    }
}
