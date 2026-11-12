// <copyright file="ModuleConfigurationValidationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.Flow;
using Workflow.Modules.Builtin.Script;
using Workflow.Modules.Transform.Script.Builtin;
using Workflow.Modules.Validation;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// §9.4 — modules' own <c>ValidateConfiguration</c> rules now run: as <b>warnings on save</b> (so a
/// half-built workflow can still be saved) and as <b>errors when a run is submitted</b>. The config is
/// shaped like run time (JSON → CLR, schema defaults applied) and <c>{{…}}</c> values aren't judged;
/// <c>compiledAssemblyKey</c> (set at publish) is only checked at run.
/// </summary>
public sealed class ModuleConfigurationValidationTests : TestKit
{
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();

    // ── Save: warnings, never blocking ───────────────────────────────────────────────────────────

    [Fact]
    public void Save_ModuleRejectingItsConfig_IsAWarningOnThatNode_NotAnError()
    {
        var result = Validate(Node("n1", "f.selfreject", ("mode", "invalid")));

        result.IsValid.Should().BeTrue(because: "on save, module config problems are warnings so drafts stay saveable (§9.4)");
        result.Warnings.Should().ContainSingle(w => w.Code == "SELF001" && w.NodeId == "n1");
    }

    [Fact]
    public void Save_MissingRequiredProperty_WarnsWithThePropertyName()
    {
        var result = Validate(Node("read", "builtin.file.read"));

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle(w => w.Code == "PATH_REQUIRED" && w.NodeId == "read" && w.Message.Contains("'path'"));
    }

    [Fact]
    public void Save_ALoopCapBelowOne_Warns_AndRunningIsBlocked()
    {
        var node = Node("loop", "builtin.loop.foreach", ("collection", new[] { 1, 2 }), ("maxIterations", 0));

        var saved = Validate(node);
        saved.IsValid.Should().BeTrue();
        saved.Warnings.Should().ContainSingle(w => w.Code == "MAX_ITERATIONS_INVALID" && w.NodeId == "loop");

        this.Submit(node).Should().BeOfType<Status.Failure>("a loop that can't run an iteration is blocked at run (§9.4)");
    }

    [Fact]
    public void Save_SchemaDefaultsAreApplied_LikeAtRunTime()
    {
        // ScriptModule's 'language' defaults to javascript, so omitting it is fine~
        Validate(Node("s", "builtin.script", ("code", "return 1;"))).Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Save_TemplatedValues_AreNotJudged()
    {
        var result = Validate(Node("read", "builtin.file.read", ("path", "{{Variable.file}}"), ("readAs", "{{Variable.mode}}")));

        result.Warnings.Should().BeEmpty(because: "a template can't be judged until it's bound at run time");
    }

    [Fact]
    public void Save_CompiledAssemblyKey_IsNotRequiredYet()
    {
        Validate(Node("t", "builtin.transform.script")).Warnings.Should().BeEmpty(
            because: "compiledAssemblyKey is set when the code is compiled at publish");
    }

    [Fact]
    public void ModuleErrors_CarryThePropertyName_NotInTheNodeIdSlot()
    {
        var errors = ModuleConfigurationChecker.Check(
            Node("sw", "builtin.switch", ("cases", Array.Empty<object>())), new SwitchModule(), atRunTime: true);

        errors.Should().ContainSingle().Which.Should().Match<ValidationError>(e => e.NodeId == "sw" && e.PropertyName == "cases");
    }

    // ── Run: errors, blocking the submit ─────────────────────────────────────────────────────────

    [Fact]
    public void Run_MissingRequiredProperty_IsRejectedBeforeStarting()
    {
        var failure = this.Submit(Node("read", "builtin.file.read"));

        failure.Should().BeOfType<Status.Failure>()
            .Which.Cause.Message.Should().Contain("validation failed").And.Contain("read").And.Contain("path is required");
    }

    [Fact]
    public void Run_CompiledAssemblyKey_IsRequired()
    {
        var failure = this.Submit(Node("t", "builtin.transform.script"));

        failure.Should().BeOfType<Status.Failure>()
            .Which.Cause.Message.Should().Contain("compiledAssemblyKey is required");
    }

    [Fact]
    public void Run_TemplatedValues_DontBlockTheRun()
    {
        // readAs only accepts text/binary/lines — a template there must not be judged before binding~
        this.Submit(Node("read", "builtin.file.read", ("path", "{{Variable.file}}"), ("readAs", "{{Variable.mode}}")))
            .Should().BeOfType<WorkflowInstanceCreated>();
    }

    [Fact]
    public async Task Api_SavesAnIncompleteWorkflow_ButRefusesToRunIt()
    {
        using var host = new SqliteHost();
        var client = host.CreateClient();
        var definition = Workflow(Node("read", "builtin.file.read"));

        var created = await client.PostAsync(
            "/api/v1/workflows",
            new StringContent(JsonSerializer.Serialize(definition, WfJson), Encoding.UTF8, "application/json"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var wfId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var run = await client.PostAsJsonAsync($"/api/v1/workflows/{wfId}/execute", new { inputs = new { } });

        run.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await run.Content.ReadAsStringAsync()).Should().Contain("path is required");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static InMemoryModuleRegistry Registry()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new SelfRejectingModule());
        registry.RegisterModule(new FileReadModule());
        registry.RegisterModule(new ScriptModule());
        registry.RegisterModule(new SwitchModule());
        registry.RegisterModule(new TransformScriptModule());
        registry.RegisterModule(new Workflow.Modules.Builtin.Flow.ForEachModule());
        return registry;
    }

    private static ValidationResult Validate(NodeDefinition node)
        => new ModuleAwareWorkflowValidator(Registry()).Validate(Workflow(node));

    private object Submit(NodeDefinition node)
    {
        var services = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(Registry())
            .AddSingleton<WorkflowValidator>()
            .BuildServiceProvider();
        var supervisor = this.Sys.ActorOf(WorkflowSupervisor.Props(services));
        supervisor.Tell(new CreateWorkflowInstance(Guid.NewGuid(), Workflow(node), HashMap<string, object?>.Empty));
        return this.ExpectMsg<object>(m => m is Status.Failure or WorkflowInstanceCreated, TimeSpan.FromSeconds(5));
    }

    private static NodeDefinition Node(string id, string moduleId, params (string Key, object? Value)[] properties)
    {
        var props = HashMap<string, JsonElement>.Empty;
        foreach (var (key, value) in properties)
        {
            props = props.AddOrUpdate(key, JsonSerializer.SerializeToElement(value));
        }

        return new NodeDefinition(id, moduleId, id, props);
    }

    private static WorkflowDefinition Workflow(NodeDefinition node)
        => new(
            Id: Guid.NewGuid(),
            Name: "config-validation-" + Guid.NewGuid().ToString("N")[..8],
            Description: null,
            Version: new Version(1, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>A module whose own configuration check always fails.</summary>
    private sealed class SelfRejectingModule : IWorkflowModule
    {
        public string ModuleId => "f.selfreject";

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Rejects its own configuration";

        public string Icon => "🚫";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr.create(new ModulePropertyDefinition("mode", "Mode", typeof(string))));

        public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
            => ValidationResult.Failure(new ValidationError("SELF001", "configuration is not acceptable"));

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" }));
    }

    private sealed class SqliteHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "sqlite",
                    ["Persistence:ConnectionString"] = global::Workflow.Tests.TestSqlite.InMemory(),
                });
            });
        }
    }
}
