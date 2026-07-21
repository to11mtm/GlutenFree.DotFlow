// <copyright file="ModuleVersioningTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Versioning;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.State;
using Workflow.Modules.Versioning;
using Xunit;

/// <summary>
/// 🔢 Phase 2.8.2 — Tests for side-by-side versioning, enabled state, state stores, and the schema comparer~ ✨.
/// </summary>
public sealed class ModuleVersioningTests
{
    // ---------- Registry versioning ----------
    [Fact]
    public void Registry_TwoVersions_Coexist()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        registry.RegisterModule(new StubModule("m", new Version(2, 0, 0)));

        registry.GetModuleVersions("m").Should().BeEquivalentTo(new[] { new Version(1, 0, 0), new Version(2, 0, 0) });
    }

    [Fact]
    public void Registry_GetLatest_ReturnsNewestEnabled()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        registry.RegisterModule(new StubModule("m", new Version(2, 0, 0)));

        registry.GetModule("m")!.Version.Should().Be(new Version(2, 0, 0));
    }

    [Fact]
    public void Registry_GetExactVersion_Works()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        registry.RegisterModule(new StubModule("m", new Version(2, 0, 0)));

        registry.GetModule("m", new Version(1, 0, 0))!.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public void Registry_GetVersions_Ascending()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(2, 0, 0)));
        registry.RegisterModule(new StubModule("m", new Version(1, 5, 0)));
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));

        registry.GetModuleVersions("m").Should().ContainInOrder(new Version(1, 0, 0), new Version(1, 5, 0), new Version(2, 0, 0));
    }

    [Fact]
    public void Registry_LegacySingleArgLookup_Unchanged()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("only"));

        registry.GetModule("only").Should().NotBeNull();
        registry.HasModule("only").Should().BeTrue();
        registry.GetAllModules().Should().ContainSingle(m => m.ModuleId == "only");
    }

    [Fact]
    public void Registry_DisabledVersion_SkippedByLatest()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        registry.RegisterModule(new StubModule("m", new Version(2, 0, 0)));

        registry.SetModuleEnabled("m", new Version(2, 0, 0), false).Should().BeTrue();

        registry.GetModule("m")!.Version.Should().Be(new Version(1, 0, 0));
        registry.GetModule("m", new Version(2, 0, 0)).Should().BeNull("disabled versions are not resolvable");
    }

    [Fact]
    public void Registry_SameVersionDuplicate_Throws()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));

        var act = () => registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        act.Should().Throw<InvalidOperationException>();
    }

    // ---------- State stores ----------
    [Fact]
    public async Task FileStateStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "wfmod-state-" + Guid.NewGuid().ToString("N"), "state.json");
        try
        {
            var store = new FileModuleStateStore(path);
            var snapshot = new ModuleStateSnapshot(new[] { new ModuleStateRecord("m", "1.0.0", false) });

            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync();

            loaded.Modules.Should().ContainSingle();
            loaded.Modules[0].ModuleId.Should().Be("m");
            loaded.Modules[0].Enabled.Should().BeFalse();
        }
        finally
        {
            var dir = Path.GetDirectoryName(path)!;
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RepositoryStateStore_RoundTrips()
    {
        var persistence = new InMemoryStatePersistence();
        var store = new RepositoryModuleStateStore(persistence);
        var snapshot = new ModuleStateSnapshot(new[] { new ModuleStateRecord("m", "2.0.0", true) });

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        loaded.Modules.Should().ContainSingle();
        loaded.Modules[0].Version.Should().Be("2.0.0");
    }

    [Fact]
    public void StateStore_RepositoryWithoutProvider_FallsBackToFileWithWarning()
    {
        var path = ModuleStateStoreFactory.DefaultStateFilePath(Path.Combine(Path.GetTempPath(), "wfmod-" + Guid.NewGuid().ToString("N")));

        var store = ModuleStateStoreFactory.Create(ModuleStateStoreFactory.RepositoryMode, path, persistence: null);

        store.Should().BeOfType<FileModuleStateStore>();
    }

    [Fact]
    public void StateStore_RepositoryWithProvider_UsesRepository()
    {
        var path = ModuleStateStoreFactory.DefaultStateFilePath(Path.Combine(Path.GetTempPath(), "wfmod-" + Guid.NewGuid().ToString("N")));

        var store = ModuleStateStoreFactory.Create(ModuleStateStoreFactory.RepositoryMode, path, new InMemoryStatePersistence());

        store.Should().BeOfType<RepositoryModuleStateStore>();
    }

    // ---------- Schema comparer ----------
    [Fact]
    public void SchemaComparer_NoChange_NoWarnings()
    {
        var schema = new ModuleSchema(
            Arr.create(new PortDefinition("in", "In", typeof(string))),
            Arr<PortDefinition>.Empty,
            Arr<ModulePropertyDefinition>.Empty);

        ModuleSchemaComparer.Compare(schema, schema).Should().BeEmpty();
    }

    [Fact]
    public void SchemaComparer_BreakingChange_Warns()
    {
        var oldSchema = new ModuleSchema(
            Arr.create(new PortDefinition("in", "In", typeof(string))),
            Arr<PortDefinition>.Empty,
            Arr<ModulePropertyDefinition>.Empty);
        var newSchema = new ModuleSchema(
            Arr.create(new PortDefinition("in", "In", typeof(int))),
            Arr<PortDefinition>.Empty,
            Arr.create(new ModulePropertyDefinition("required2", "R", typeof(string), IsRequired: true)));

        var warnings = ModuleSchemaComparer.Compare(oldSchema, newSchema);

        warnings.Should().Contain(w => w.Contains("changed type"));
        warnings.Should().Contain(w => w.Contains("required"));
    }

    // ---------- Validator pin check ----------
    [Fact]
    public void Validator_PinnedVersionMissing_ReportsCode()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        var validator = new Workflow.Modules.Validation.ModuleAwareWorkflowValidator(registry);

        var node = new NodeDefinition(
            Id: "n1",
            ModuleId: "m",
            Name: "N1",
            Properties: HashMap<string, System.Text.Json.JsonElement>.Empty,
            Metadata: HashMap.create(("moduleVersion", "9.9.9")));

        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "wf",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var result = validator.Validate(workflow);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MA003");
    }

    [Fact]
    public void Validator_PinnedVersionPresent_Passes()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new StubModule("m", new Version(1, 0, 0)));
        var validator = new Workflow.Modules.Validation.ModuleAwareWorkflowValidator(registry);

        var node = new NodeDefinition(
            Id: "n1",
            ModuleId: "m",
            Name: "N1",
            Properties: HashMap<string, System.Text.Json.JsonElement>.Empty,
            Metadata: HashMap.create(("moduleVersion", "1.0.0")));

        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "wf",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        validator.Validate(workflow).Errors.Should().NotContain(e => e.Code == "MA003");
    }

    private sealed class InMemoryStatePersistence : IModuleStatePersistence
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
