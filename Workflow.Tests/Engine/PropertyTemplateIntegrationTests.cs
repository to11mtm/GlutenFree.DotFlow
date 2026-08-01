// <copyright file="PropertyTemplateIntegrationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// 🔗 Phase 3.5 (V4) — end-to-end proof that a <c>{{…}}</c> token authored on a node
/// <b>property</b> is resolved by the time the module runs.
/// </summary>
/// <remarks>
/// There was no integration coverage of this at all before V4 — which is precisely how the gap
/// survived: <c>NodeExecutor</c> bound only <c>module.Schema.Inputs</c> and handed properties
/// through raw, while the designer cheerfully offered a <c>{{x}}</c> picker on those same
/// properties.
/// </remarks>
public sealed class PropertyTemplateIntegrationTests : TestKit
{
    [Fact]
    public async Task TemplatedProperty_IsResolvedBeforeTheModuleRuns()
    {
        var module = new PropertyCaptureModule();
        var workflow = WorkflowWith(
            properties: ("url", "{{Variable.host}}/orders"),
            variables: new VariableDefinition("host", PropertyType.String, Json("\"https://api.example.com\"")));

        var status = await Run(module, workflow);

        status.State.Should().Be(ExecutionState.Completed, because: status.Error.IfNone("no error"));
        module.Seen["url"].Should().Be("https://api.example.com/orders");
    }

    [Fact]
    public async Task NonTemplatedProperty_ReachesTheModuleVerbatim()
    {
        var module = new PropertyCaptureModule();
        var workflow = WorkflowWith(
            properties: ("sql", "SELECT {{Variable.host}}"),
            variables: new VariableDefinition("host", PropertyType.String, Json("\"nope\"")));

        await Run(module, workflow);

        module.Seen["sql"].Should().Be("SELECT {{Variable.host}}", because: "SQL text must never be rewritten");
    }

    [Fact]
    public async Task UnresolvableReference_FailsTheNode()
    {
        var module = new PropertyCaptureModule();
        var workflow = WorkflowWith(properties: ("url", "{{Variable.doesNotExist}}"));

        var status = await Run(module, workflow);

        status.State.Should().Be(ExecutionState.Failed);
        status.Error.IsSome.Should().BeTrue();
        status.Error.IfNone(string.Empty).Should().Contain("doesNotExist");
    }

    [Fact]
    public async Task EscapedBraces_ReachTheModuleAsALiteral()
    {
        var module = new PropertyCaptureModule();
        var workflow = WorkflowWith(properties: ("url", @"\{\{literal}}"));

        var status = await Run(module, workflow);

        status.State.Should().Be(ExecutionState.Completed);
        module.Seen["url"].Should().Be("{{literal}}");
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private async Task<WorkflowStatusResponse> Run(IWorkflowModule module, WorkflowDefinition workflow)
    {
        var services = new ServiceCollection();
        services.AddSingleton<WorkflowValidator>();
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(module);
        services.AddSingleton<IModuleRegistry>(registry);

        var supervisor = Sys.ActorOf(WorkflowSupervisor.Props(services.BuildServiceProvider()));
        supervisor.Tell(new CreateWorkflowInstance(
            workflow.Id,
            workflow,
            HashMap<string, object?>.Empty,
            new ExecutionStartOptions("tester", VariableWriteMode.Execution)));
        var created = ExpectMsg<WorkflowInstanceCreated>(TimeSpan.FromSeconds(5));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            supervisor.Tell(new GetWorkflowStatus(created.ExecutionId));
            var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(5));
            if (status.State is ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
            {
                return status;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Execution did not reach a terminal state.");
    }

    private static WorkflowDefinition WorkflowWith(
        (string Name, string Value) properties,
        params VariableDefinition[] variables)
    {
        var node = new NodeDefinition(
            Id: "node_1",
            ModuleId: "test.propcapture",
            Name: "Capture",
            Properties: new[] { (properties.Name, JsonSerializer.SerializeToElement(properties.Value)) }.ToHashMap());

        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Property template workflow",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(node),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: variables.Select(v => (v.Name, v)).ToHashMap(),
            Trigger: null,
            ErrorHandling: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Tags: null);
    }

    /// <summary>
    /// Declares one template-enabled property (<c>url</c>) and one deliberately literal one
    /// (<c>sql</c>), then records what actually arrived~ 🔍.
    /// </summary>
    private sealed class PropertyCaptureModule : IWorkflowModule
    {
        public ConcurrentDictionary<string, object?> Seen { get; } = new();

        public string ModuleId => "test.propcapture";

        public string DisplayName => "Property capture";

        public string Category => "Testing";

        public string Description => "Records the properties the engine handed over.";

        public string Icon => "🔍";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr.create(
                new ModulePropertyDefinition(
                    "url", "URL", typeof(string), "Template-enabled", SupportsTemplates: true),
                new ModulePropertyDefinition(
                    "sql", "SQL", typeof(string), "Deliberately literal")));

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            foreach (var (name, value) in context.Properties)
            {
                this.Seen[name] = value;
            }

            return Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = true }));
        }
    }
}
