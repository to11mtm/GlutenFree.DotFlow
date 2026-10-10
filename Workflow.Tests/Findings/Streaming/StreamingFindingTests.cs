// <copyright file="StreamingFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Streaming;

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// Streaming findings from <c>docs/architecture/03-streaming-and-spill.md §9</c> that are testable
/// at the engine level. In-memory actors and stream stubs only.
/// </summary>
[Trait("Category", FindingDefs.Category)]
public sealed class StreamingFindingTests : TestKit
{
    private ExecutionState AwaitTerminal(IActorRef executor, Guid executionId, TimeSpan? timeout = null)
    {
        var state = ExecutionState.Pending;
        this.AwaitAssert(
            () =>
            {
                executor.Tell(new GetWorkflowStatus(executionId));
                var status = this.ExpectMsg<WorkflowStatusResponse>(TimeSpan.FromSeconds(3));
                state = status.State;
                state.Should().BeOneOf(ExecutionState.Completed, ExecutionState.Failed);
            },
            timeout ?? FindingDefs.RunTimeout,
            TimeSpan.FromMilliseconds(200));
        return state;
    }

    // ── Finding 03 §9.4 — pinned module versions are ignored inside regions ───────────────────────

    /// <summary>
    /// A streaming source node pinned to version 1.0 (via <c>Metadata["moduleVersion"]</c>) should
    /// run that version. Two versions of the source are registered (1.0 emits "v1", 2.0 emits "v2");
    /// 2.0 is the latest. <c>StreamRegionExecutor</c> resolves modules with <c>GetModule(id)</c>
    /// (latest) rather than honouring the pin, so it runs 2.0.
    /// </summary>
    [Fact]
    public void PinnedModuleVersion_ShouldBeHonoured_InsideRegion()
    {
        var v1 = new StreamSourceModule("f.pinsrc", new[] { "v1" }, new Version(1, 0));
        var v2 = new StreamSourceModule("f.pinsrc", new[] { "v2" }, new Version(2, 0));
        var collector = new CollectingTerminalModule("f.pincollect");

        var registry = new Workflow.Modules.InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(v1);
        registry.RegisterModule(v2);
        registry.RegisterModule(collector);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        var definition = FindingDefs.Definition(
            nodes: new[]
            {
                FindingDefs.Node("src", "f.pinsrc", metadata: HashMap<string, string>.Empty.Add("moduleVersion", "1.0")),
                FindingDefs.Node("collect", "f.pincollect"),
            },
            connections: new[] { FindingDefs.Edge("src", "items", "collect", "items") });

        var executionId = Guid.NewGuid();
        var executor = this.Sys.ActorOf(
            WorkflowExecutor.Props(executionId, definition, new Dictionary<string, object?>(), services),
            "pin-exec");
        executor.Tell(new StartExecution(executionId));
        this.AwaitTerminal(executor, executionId).Should().Be(ExecutionState.Completed);

        collector.Received.Should().ContainSingle().Which.Should().Be(
            "v1",
            because: "the source node pinned to version 1.0 should run that version inside the region (03 §9.4)");
    }
}
