// <copyright file="NodeRecordScopeTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Workflow.Api.Contracts;
using Workflow.Core.Models;
using Workflow.Engine.Models;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// The <c>/nodes</c> record carries the full scope a body node ran in (outermost first), whether
/// the record's metadata holds <see cref="ScopeStep"/>s (written in memory) or JSON (read back from a store).
/// </summary>
public sealed class NodeRecordScopeTests
{
    private static NodeExecutionRecord Record(object? scopePath)
        => new(
            ExecutionId: Guid.NewGuid(),
            NodeId: "body",
            State: NodeExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow,
            Metadata: scopePath is null ? null : new Dictionary<string, object?> { ["scopePath"] = scopePath });

    private static readonly ScopeStep[] Steps =
    {
        new("outer", ScopeStep.Loop, Iteration: 2),
        new("par", ScopeStep.Parallel, BranchIndex: 1),
        new("tc", ScopeStep.TryCatch, Branch: "catch"),
    };

    [Fact]
    public void ScopeSteps_InMemory_AreProjected()
    {
        var dto = NodeExecutionRecordDto.From(Record(Steps));

        dto.ScopePath.Should().Be("outer#2/par[1]/tc:catch");
        dto.Scope.Should().BeEquivalentTo(new[]
        {
            new ScopeStepDto("outer", "loop", 2, null, null),
            new ScopeStepDto("par", "parallel", null, 1, null),
            new ScopeStepDto("tc", "trycatch", null, null, "catch"),
        });
    }

    [Fact]
    public void ScopeSteps_ReadBackFromAStore_AreProjected()
    {
        // Stores keep metadata as JSON (camelCase, nulls left out) and hand back JsonElements.
        var stored = JsonSerializer.SerializeToElement(Steps, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var dto = NodeExecutionRecordDto.From(Record(stored));

        dto.ScopePath.Should().Be("outer#2/par[1]/tc:catch");
        dto.Scope![0].Should().Be(new ScopeStepDto("outer", "loop", 2, null, null));
    }

    [Fact]
    public void ATopLevelRecord_HasNoScope()
    {
        var dto = NodeExecutionRecordDto.From(Record(null));

        dto.ScopePath.Should().BeNull();
        dto.Scope.Should().BeNull();
    }
}
