// <copyright file="SecretPersistenceFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Binding;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// Finding 02 §10.6 — secret values reach persisted node history in plain text.
/// </summary>
/// <remarks>
/// A workflow run input named after a secret variable is copied into every node's input dictionary
/// and persisted verbatim as <c>NodeExecutionRecord.Inputs</c>. This test asserts the SHOULD:
/// a value belonging to a variable declared <c>IsSecret</c> must not be written to history in the
/// clear. There is no redaction policy today, so this test fails until one is added — that failure
/// is the finding. It uses an in-memory capturing repository; no real persistence.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class SecretPersistenceFindingTests : TestKit
{
    private const string SecretValue = "pretend-secret-token-value";

    [Fact]
    public void SecretValue_ShouldNotBePersisted_InNodeHistoryInputs()
    {
        var records = new ConcurrentBag<NodeExecutionRecord>();
        var sink = new CaptureModule("f.secretsink", inputPorts: new[] { "apiKey" });

        var registry = new Workflow.Modules.InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(sink);

        var services = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .AddSingleton<IExecutionHistoryRepository>(new CapturingHistory(records))
            .BuildServiceProvider();

        // apiKey is both a declared secret variable and a run input of the same name.
        var definition = FindingDefs.Definition(
            nodes: new[] { FindingDefs.Node("sink", "f.secretsink") },
            variables: new[]
            {
                new VariableDefinition("apiKey", PropertyType.String, FindingDefs.Json("placeholder"), IsSecret: true),
            });

        var id = Guid.NewGuid();
        var parent = this.CreateTestProbe("secret-parent");
        var executor = parent.ChildActorOf(
            WorkflowExecutor.Props(id, definition, new Dictionary<string, object?> { ["apiKey"] = SecretValue }, services),
            "secret-exec");
        executor.Tell(new StartExecution(id));
        parent.FishForMessage(m => m is WorkflowCompleted or WorkflowFailed, FindingDefs.RunTimeout);

        // Allow the fire-and-forget persistence writes to land.
        SpinWait.SpinUntil(() => records.Any(r => r.NodeId == "sink"), TimeSpan.FromSeconds(3));

        var persistedInputs = records
            .Where(r => r.NodeId == "sink" && r.Inputs is not null)
            .SelectMany(r => r.Inputs!.Values)
            .Select(v => v?.ToString());

        persistedInputs.Should().NotContain(
            SecretValue,
            because: "a secret-flagged value should be redacted before it is written to execution history (02 §10.6)");
    }

    private sealed class CapturingHistory : IExecutionHistoryRepository
    {
        private readonly ConcurrentBag<NodeExecutionRecord> records;

        public CapturingHistory(ConcurrentBag<NodeExecutionRecord> records) => this.records = records;

        public Task<Guid> CreateExecutionAsync(ExecutionRecord record, CancellationToken ct = default)
            => Task.FromResult(record.ExecutionId);

        public Task RecordNodeExecutionAsync(NodeExecutionRecord nodeRecord, CancellationToken ct = default)
        {
            this.records.Add(nodeRecord);
            return Task.CompletedTask;
        }

        public Task UpdateExecutionStatusAsync(Guid executionId, ExecutionState state, DateTimeOffset? endTime = null, string? error = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<NodeExecutionRecord>> GetNodeExecutionsAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<NodeExecutionRecord>>(this.records.ToList());

        public Task<ExecutionRecord?> GetExecutionAsync(Guid executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionRecord?>(null);

        public Task<PagedResult<ExecutionRecord>> GetExecutionsForWorkflowAsync(Guid workflowId, ExecutionFilter filter, Pagination pagination, CancellationToken ct = default)
            => Task.FromResult(new PagedResult<ExecutionRecord>(new List<ExecutionRecord>(), 0, pagination.Page, pagination.PageSize));
    }
}
