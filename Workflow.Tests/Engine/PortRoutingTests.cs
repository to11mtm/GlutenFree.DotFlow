// <copyright file="PortRoutingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// Tests for Phase 2.2.0a port-aware connection routing~ 🎯✨
/// Validates that <see cref="WorkflowExecutor"/> correctly activates/skips downstream branches
/// based on <see cref="NodeExecutionCompleted.ActivePorts"/>.
/// </summary>
public class PortRoutingTests : TestKit
{
    /// <summary>
    /// Active-port routing: only the "true" branch fires when condition is true~
    /// </summary>
    [Fact]
    public void ActivePorts_TrueOnly_FiresOnlyTrueConnection()
    {
        // Arrange — condition → [trueNode, falseNode]; condition activates only "true"
        var conditionModule = new StubPortActivatingModule("condition", activePorts: new[] { "true" },
            declaredPorts: new[] { "true", "false" },
            outputs: new Dictionary<string, object?> { ["result"] = true });
        var trueModule = new StubPassthroughModule("builtin.pass.true");
        var falseModule = new StubPassthroughModule("builtin.pass.false");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(conditionModule);
        registry.RegisterModule(trueModule);
        registry.RegisterModule(falseModule);

        var services = new ServiceCollection();
        services.AddSingleton<IModuleRegistry>(registry);
        var sp = services.BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "port-routing-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("cond",      "condition",         "Condition",   HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("nodeTrue",  "builtin.pass.true", "True Branch", HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("nodeFalse", "builtin.pass.false","False Branch",HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("cond", "true",  "nodeTrue",  "input"),
                new ConnectionDefinition("cond", "false", "nodeFalse", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so WorkflowCompleted is routed here, not to user guardian~ 🎯
        var parentProbe = CreateTestProbe("port-routing-true-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "port-routing-true-test");

        // Act
        executor.Tell(new StartExecution(executionId));

        // Assert — workflow completes (trueNode runs, falseNode is skipped)
        // CopilotNote: WorkflowExecutor sends both ExecutionStateChanged AND terminal messages to Parent.
        // Use FishForMessage to skip state events and grab WorkflowCompleted~ 🐟
        var completed = (WorkflowCompleted)parentProbe.FishForMessage(
            m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));
        completed.Should().NotBeNull();

        // Verify node states (GetWorkflowStatus replies to Sender, not Parent — so ExpectMsg works here)
        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        status.NodeStates.Find("cond").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed);
        status.NodeStates.Find("nodeTrue").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed);
        status.NodeStates.Find("nodeFalse").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Skipped,
            because: "the 'false' port was not activated by the condition module");
    }

    /// <summary>
    /// Active-port routing: only the "false" branch fires when condition is false~
    /// </summary>
    [Fact]
    public void ActivePorts_FalseOnly_FiresOnlyFalseConnection()
    {
        var conditionModule = new StubPortActivatingModule("condition.false", activePorts: new[] { "false" },
            declaredPorts: new[] { "true", "false" },
            outputs: new Dictionary<string, object?> { ["result"] = false });
        var trueModule = new StubPassthroughModule("builtin.pass.t2");
        var falseModule = new StubPassthroughModule("builtin.pass.f2");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(conditionModule);
        registry.RegisterModule(trueModule);
        registry.RegisterModule(falseModule);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "port-routing-false-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("cond",      "condition.false",  "Condition",   HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("nodeTrue",  "builtin.pass.t2",  "True Branch", HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("nodeFalse", "builtin.pass.f2",  "False Branch",HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("cond", "true",  "nodeTrue",  "input"),
                new ConnectionDefinition("cond", "false", "nodeFalse", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so WorkflowCompleted is routed here, not to user guardian~ 🎯
        var parentProbe = CreateTestProbe("port-routing-false-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "port-routing-false-test");

        executor.Tell(new StartExecution(executionId));

        // CopilotNote: Fish past ExecutionStateChanged messages to get WorkflowCompleted~ 🐟
        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));

        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        status.NodeStates.Find("nodeTrue").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Skipped,
            because: "the 'true' port was not activated");
        status.NodeStates.Find("nodeFalse").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed);
    }

    /// <summary>
    /// Backwards-compat: nodes without ActivePorts fire ALL outgoing connections (legacy default)~
    /// </summary>
    [Fact]
    public void NoActivePorts_FiresAllConnections_BackwardsCompatible()
    {
        var sourceModule = new StubPassthroughModule("source");
        var branch1Module = new StubPassthroughModule("branch1");
        var branch2Module = new StubPassthroughModule("branch2");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(sourceModule);
        registry.RegisterModule(branch1Module);
        registry.RegisterModule(branch2Module);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "backwards-compat-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("src",  "source",  "Source",   HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("b1",   "branch1", "Branch 1", HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("b2",   "branch2", "Branch 2", HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("src", "output", "b1", "input"),
                new ConnectionDefinition("src", "output", "b2", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so WorkflowCompleted is routed here, not to user guardian~ 🎯
        var parentProbe = CreateTestProbe("backwards-compat-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "backwards-compat-test");

        executor.Tell(new StartExecution(executionId));

        // CopilotNote: Fish past ExecutionStateChanged messages to get WorkflowCompleted~ 🐟
        parentProbe.FishForMessage(m => m is WorkflowCompleted, TimeSpan.FromSeconds(5));

        executor.Tell(new GetWorkflowStatus(executionId));
        var status = ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));

        status.NodeStates.Find("b1").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed,
            because: "fire-all semantics: both branches should run");
        status.NodeStates.Find("b2").IfNone(NodeExecutionState.Pending).Should().Be(NodeExecutionState.Completed);
    }

    /// <summary>
    /// Load-time port validation: connection referencing undeclared SourcePort fails immediately~
    /// </summary>
    [Fact]
    public void UndeclaredSourcePort_FailsWorkflowAtLoadTime()
    {
        // Module declares only "output" port, but connection uses "undeclaredPort"
        var strictModule = new StubStrictPortModule("strict.module", declaredOutputPort: "output");
        var downstreamModule = new StubPassthroughModule("downstream");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(strictModule);
        registry.RegisterModule(downstreamModule);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "bad-port-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("src",  "strict.module", "Source",     HashMap<string, System.Text.Json.JsonElement>.Empty),
                new NodeDefinition("dst",  "downstream",    "Downstream", HashMap<string, System.Text.Json.JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("src", "undeclaredPort", "dst", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var executionId = Guid.NewGuid();

        // CopilotNote: Use TestProbe as parent so WorkflowFailed is routed here, not to user guardian~ 🛡️
        var parentProbe = CreateTestProbe("bad-port-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), sp),
            "bad-port-test");

        // Act — start should fail fast
        executor.Tell(new StartExecution(executionId));

        // Assert — workflow fails with a validation error (not completes)
        // CopilotNote: Fish past ExecutionStateChanged messages to get WorkflowFailed~ 🐟
        var failed = (WorkflowFailed)parentProbe.FishForMessage(
            m => m is WorkflowFailed, TimeSpan.FromSeconds(5));
        failed.Error.Message.Should().Contain("undeclaredPort",
            because: "the port validation error should name the offending port");
    }

    // ── Test Module Stubs ────────────────────────────────────────────────────────────────────────

    /// <summary>Module that returns specified ActivePorts for conditional routing tests~ 🎯</summary>
    private sealed class StubPortActivatingModule : IWorkflowModule
    {
        private readonly string[] _activePorts;
        private readonly string[] _declaredPorts;
        private readonly Dictionary<string, object?> _outputs;

        /// <summary>
        /// Creates the stub. <paramref name="declaredPorts"/> defines the schema outputs (what CAN fire),
        /// while <paramref name="activePorts"/> defines what DOES fire at runtime.
        /// CopilotNote: Schema must declare ALL ports that connections reference, even inactive ones~ 🛡️
        /// </summary>
        public StubPortActivatingModule(string moduleId, string[] activePorts,
            string[]? declaredPorts = null,
            Dictionary<string, object?>? outputs = null)
        {
            ModuleId = moduleId;
            _activePorts = activePorts;
            _declaredPorts = declaredPorts ?? activePorts;
            _outputs = outputs ?? new Dictionary<string, object?>();
        }

        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Test stub with active ports";
        public string Icon => "🎯";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: _declaredPorts
                .Select(p => PortDefinition.Create<object>(p, isRequired: false))
                .Append(PortDefinition.Create<object>("result", isRequired: false))
                .ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.WithActivePorts(_outputs, _activePorts));
    }

    /// <summary>Module that passes through with no ActivePorts (fire-all legacy behaviour)~ 🌿</summary>
    private sealed class StubPassthroughModule : IWorkflowModule
    {
        public StubPassthroughModule(string moduleId) => ModuleId = moduleId;

        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Pass-through test stub";
        public string Icon => "🌿";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: new[] { PortDefinition.Create<object>("input", isRequired: false) }.ToArr(),
            Outputs: new[] { PortDefinition.Create<object>("output", isRequired: false) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "done" }));
    }

    /// <summary>Module with strictly declared ports for load-time validation test~ 🛡️</summary>
    private sealed class StubStrictPortModule : IWorkflowModule
    {
        private readonly string _declaredOutputPort;

        public StubStrictPortModule(string moduleId, string declaredOutputPort)
        {
            ModuleId = moduleId;
            _declaredOutputPort = declaredOutputPort;
        }

        public string ModuleId { get; }
        public string DisplayName => ModuleId;
        public string Category => "Test";
        public string Description => "Strict-port test stub";
        public string Icon => "🛡️";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: new[] { PortDefinition.Create<object>(_declaredOutputPort) }.ToArr(),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { [_declaredOutputPort] = "done" }));
    }
}













