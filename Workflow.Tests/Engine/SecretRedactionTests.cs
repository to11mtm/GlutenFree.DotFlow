// <copyright file="SecretRedactionTests.cs" company="GlutenFree">
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
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Tests.Findings;
using Xunit;

/// <summary>
/// §10.6 — values of variables declared <c>IsSecret</c> are hidden (by name and by value) before
/// they reach run history, the run's variable-write records, and the failure events sent to live
/// clients. Non-secret values are left alone.
/// </summary>
public sealed class SecretRedactionTests : TestKit
{
    private const string Secret = "sk-live-1234567890";
    private const string Rotated = "sk-rotated-0987654321";
    private const string Mask = "••••••";

    [Fact]
    public void TopLevel_EverySinkHidesTheSecret_ByNameAndByValue()
    {
        var (history, variables, events) = (new CapturingHistory(), new CapturingVariableStore(), this.CreateTestProbe());
        this.Sys.EventStream.Subscribe(events.Ref, typeof(WorkflowFailed));
        this.Sys.EventStream.Subscribe(events.Ref, typeof(NodeExecutionFailed));

        // call (echoes the key inside a URL) → rotate (writes a new secret + a normal variable) → fail
        // (error text carries both the URL and the rotated key).
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("call", "sr.call"),
                FindingDefs.Node("rotate", "sr.rotate"),
                FindingDefs.Node("fail", "sr.fail"),
            },
            connections: new[]
            {
                FindingDefs.Edge("call", "url", "rotate", "input"),
                FindingDefs.Edge("rotate", "output", "fail", "url"),
            },
            variables: new[]
            {
                new VariableDefinition("apiKey", PropertyType.String, FindingDefs.Json("placeholder"), IsSecret: true),
                new VariableDefinition("region", PropertyType.String, FindingDefs.Json("us")),
            });

        var terminal = this.Run(
            definition,
            new Dictionary<string, object?> { ["apiKey"] = Secret },
            history,
            variables,
            new OutputModule("sr.call", new Dictionary<string, object?> { ["url"] = $"https://api.example/x?key={Secret}", ["status"] = 200 }),
            new WriteModule("sr.rotate", ("apiKey", Rotated), ("region", "eu")),
            new EchoFailModule("sr.fail"));

        terminal.Should().BeOfType<WorkflowFailed>();
        history.WaitFor(r => r.NodeId == "fail");

        using var _ = new FluentAssertions.Execution.AssertionScope();

        // Execution record: the run input is masked by name.
        history.Created.Single().Inputs!["apiKey"].Should().Be(Mask);

        // Node records: keys by name, echoed values by value; everything else intact.
        var call = history.Nodes.Single(r => r.NodeId == "call");
        call.Inputs!["apiKey"].Should().Be(Mask);
        call.Outputs!["url"].Should().Be($"https://api.example/x?key={Mask}");
        call.Outputs!["status"].Should().Be(200);

        // Variable-write records for this run: the secret is masked, a normal variable isn't.
        variables.Writes.Should().Contain(w => w.Name == "apiKey" && Equals(w.Value, Mask));
        variables.Writes.Should().Contain(w => w.Name == "region" && Equals(w.Value, "eu"));
        variables.Writes.Select(w => w.Value as string ?? string.Empty)
            .Should().NotContain(s => s.Contains(Secret) || s.Contains(Rotated));

        // Errors: in history, in the run's status, and in the events live clients get.
        var failed = history.Nodes.Single(r => r.NodeId == "fail");
        failed.Error.Should().Contain(Mask).And.NotContain(Secret).And.NotContain(Rotated, because: "a secret written mid-run is learned too");
        history.StatusErrors.Should().NotBeEmpty().And.OnlyContain(e => !e.Contains(Secret) && !e.Contains(Rotated));

        var published = events.ReceiveWhile<object>(TimeSpan.FromSeconds(1), m => m, 5);
        published.OfType<NodeExecutionFailed>().Single().Error.Message.Should().Contain(Mask).And.NotContain(Secret);
        published.OfType<WorkflowFailed>().Single().Error.Message.Should().NotContain(Secret).And.NotContain(Rotated);

        // Nothing anywhere in history holds a secret.
        history.AllText().Should().NotContain(Secret).And.NotContain(Rotated);
    }

    [Fact]
    public void BodyRecords_HideTheSecret_IncludingOneWrittenInsideTheBody()
    {
        const string BodySecret = "sk-body-5555555555";
        var history = new CapturingHistory();
        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("loop", "builtin.loop.foreach", properties: new (string, object?)[] { ("collection", new[] { "a" }) }),
                FindingDefs.Node("rotate", "sr.rotate"),
                FindingDefs.Node("fail", "sr.fail"),
            },
            connections: new[]
            {
                FindingDefs.Edge("loop", "loopBody", "rotate", "input"),
                FindingDefs.Edge("rotate", "output", "fail", "url"),
            },
            variables: new[] { new VariableDefinition("apiKey", PropertyType.String, FindingDefs.Json("placeholder"), IsSecret: true) });

        this.Run(
            definition,
            new Dictionary<string, object?> { ["apiKey"] = Secret },
            history,
            null,
            new ForEachModule(),
            new WriteModule("sr.rotate", ("apiKey", BodySecret)),
            new EchoFailModule("sr.fail"));
        history.WaitFor(r => r.NodeId == "fail");

        // (Completed body records carry no inputs today — only failed ones do; see worklog follow-ups.)
        var failed = history.Nodes.Single(r => r.NodeId == "fail");
        failed.Inputs!["apiKey"].Should().Be(Mask, because: "body nodes carry the run inputs too");
        failed.Error.Should().Contain(Mask).And.NotContain(BodySecret);
        history.AllText().Should().NotContain(Secret).And.NotContain(BodySecret);
    }

    [Fact]
    public void WithoutSecrets_NothingIsChanged()
    {
        var history = new CapturingHistory();
        var definition = FindingDefs.Definition(
            nodes: new[] { FindingDefs.Node("call", "sr.call") },
            variables: new[] { new VariableDefinition("apiKey", PropertyType.String, FindingDefs.Json("x")) });

        this.Run(
            definition,
            new Dictionary<string, object?> { ["apiKey"] = Secret },
            history,
            null,
            new OutputModule("sr.call", new Dictionary<string, object?> { ["url"] = $"https://api.example/x?key={Secret}" }));
        history.WaitFor(r => r.NodeId == "call");

        var call = history.Nodes.Single(r => r.NodeId == "call");
        call.Inputs!["apiKey"].Should().Be(Secret);
        call.Outputs!["url"].Should().Be($"https://api.example/x?key={Secret}");
    }

    private object Run(
        WorkflowDefinition definition,
        Dictionary<string, object?> inputs,
        CapturingHistory history,
        CapturingVariableStore? variables,
        params IWorkflowModule[] modules)
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        foreach (var module in modules)
        {
            registry.RegisterModule(module);
        }

        var services = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(history);
        if (variables is not null)
        {
            services.AddSingleton<IVariableStore>(variables);
        }

        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe();
        var executor = parent.ChildActorOf(WorkflowExecutor.Props(id, definition, inputs, services.BuildServiceProvider()));
        executor.Tell(new StartExecution(id));
        return parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);
    }

    /// <summary>Writes the given variables; passes its input through as 'output'.</summary>
    private sealed class WriteModule : FindingModuleBaseShim
    {
        private readonly (string Name, object? Value)[] writes;

        public WriteModule(string id, params (string Name, object? Value)[] writes)
            : base(id, new[] { "input" }, new[] { "output" }) => this.writes = writes;

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(
                new Dictionary<string, object?> { ["output"] = context.Inputs.TryGetValue("input", out var v) ? v : null },
                this.writes.ToDictionary(w => w.Name, w => w.Value)));
    }

    /// <summary>Fails with a message quoting its 'url' input and the current apiKey variable.</summary>
    private sealed class EchoFailModule : FindingModuleBaseShim
    {
        public EchoFailModule(string id)
            : base(id, new[] { "url" }, new[] { "output" })
        {
        }

        public override Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.Inputs.TryGetValue("url", out var url);
            context.Variables.TryGetValue("apiKey", out var key);
            var message = $"request to {url} rejected for key {key}";
            return Task.FromResult(ModuleResult.Fail(message, new InvalidOperationException(message)));
        }
    }

    private abstract class FindingModuleBaseShim : IWorkflowModule
    {
        private readonly string[] inputs;
        private readonly string[] outputs;

        protected FindingModuleBaseShim(string id, string[] inputs, string[] outputs)
        {
            this.ModuleId = id;
            this.inputs = inputs;
            this.outputs = outputs;
        }

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "secret redaction stub";

        public string Icon => "🔒";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            this.inputs.Select(n => PortDefinition.Create<object>(n, isRequired: false)).ToArr(),
            this.outputs.Select(n => PortDefinition.Create<object>(n, isRequired: false)).ToArr(),
            Arr<ModulePropertyDefinition>.Empty);

        public abstract Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default);
    }

    private sealed class CapturingHistory : IExecutionHistoryRepository
    {
        public ConcurrentQueue<ExecutionRecord> Created { get; } = new();

        public ConcurrentQueue<NodeExecutionRecord> Nodes { get; } = new();

        public ConcurrentQueue<string> StatusErrors { get; } = new();

        public void WaitFor(Func<NodeExecutionRecord, bool> predicate)
            => SpinWait.SpinUntil(() => this.Nodes.Any(predicate), TimeSpan.FromSeconds(5)).Should().BeTrue();

        public string AllText()
            => JsonSerializer.Serialize(new { this.Created, this.Nodes, this.StatusErrors });

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
        {
            this.Created.Enqueue(record);
            return Task.FromResult(record.ExecutionId);
        }

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            this.Nodes.Enqueue(nodeRecord);
            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
        {
            if (error is not null)
            {
                this.StatusErrors.Enqueue(error);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(this.Nodes.ToList());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, pagination.Page, pagination.PageSize));
    }

    private sealed class CapturingVariableStore : IVariableStore
    {
        public ConcurrentQueue<(VariableScope Scope, string Name, object? Value)> Writes { get; } = new();

        public Task SetVariableAsync(VariableScope scope, string name, object? value, CancellationToken ct = default)
        {
            this.Writes.Enqueue((scope, name, value));
            return Task.CompletedTask;
        }

        public Task<VariableEntry?> GetVariableAsync(VariableScope scope, string name, int? version = null, CancellationToken ct = default)
            => Task.FromResult<VariableEntry?>(null);

        public Task<IReadOnlyList<VariableEntry>> GetVariableHistoryAsync(VariableScope scope, string name, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<VariableEntry>>(new List<VariableEntry>());

        public Task<bool> DeleteVariableAsync(VariableScope scope, string name, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, object?>> GetAllVariablesAsync(VariableScope scope, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }
}
