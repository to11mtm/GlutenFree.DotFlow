// <copyright file="AbstractionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Persistence;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Workflow.Core.Models;
using Workflow.Persistence;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Composite;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// 🔌 Phase 2.1.0 — Tests for persistence abstractions, DTOs, and composite provider~ ✨💖
/// </summary>
public sealed class AbstractionTests
{
    #region PagedResult Tests 📄

    [Fact]
    public void PagedResult_HasNextPage_ShouldBeTrueWhenMoreItems()
    {
        var result = new PagedResult<string>(new[] { "a", "b" }, TotalCount: 5, Page: 1, PageSize: 2);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_HasNextPage_ShouldBeFalseOnLastPage()
    {
        var result = new PagedResult<string>(new[] { "e" }, TotalCount: 5, Page: 3, PageSize: 2);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_TotalPages_ShouldComputeCorrectly()
    {
        var result = new PagedResult<int>(Array.Empty<int>(), TotalCount: 7, Page: 1, PageSize: 3);
        result.TotalPages.Should().Be(3); // ceil(7/3) = 3
    }

    [Fact]
    public void PagedResult_Empty_ShouldHaveZeroItems()
    {
        var result = PagedResult<string>.Empty();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    #endregion

    #region VariableScope Tests 💾

    [Fact]
    public void VariableScope_Global_ShouldHaveGlobalKind()
    {
        var scope = VariableScope.Global;
        scope.Kind.Should().Be(VariableScopeKind.Global);
        scope.WorkflowId.Should().BeNull();
        scope.ExecutionId.Should().BeNull();
    }

    [Fact]
    public void VariableScope_Global_ShouldBeSameInstance()
    {
        var a = VariableScope.Global;
        var b = VariableScope.Global;
        ReferenceEquals(a, b).Should().BeTrue("Global should be a singleton-like static property~ ✨");
    }

    [Fact]
    public void VariableScope_ForWorkflow_ShouldCarryWorkflowId()
    {
        var id = Guid.NewGuid();
        var scope = VariableScope.ForWorkflow(id);
        scope.Kind.Should().Be(VariableScopeKind.Workflow);
        scope.WorkflowId.Should().Be(id);
        scope.ExecutionId.Should().BeNull();
    }

    [Fact]
    public void VariableScope_ForExecution_ShouldCarryExecutionId()
    {
        var id = Guid.NewGuid();
        var scope = VariableScope.ForExecution(id);
        scope.Kind.Should().Be(VariableScopeKind.Execution);
        scope.ExecutionId.Should().Be(id);
        scope.WorkflowId.Should().BeNull();
    }

    #endregion

    #region Pagination Tests 📑

    [Fact]
    public void Pagination_ShouldClampPageSizeToMax()
    {
        var p = new Pagination(page: 1, pageSize: 500);
        p.PageSize.Should().Be(Pagination.MaxPageSize, "PageSize must be clamped to 200~ 🛑");
    }

    [Fact]
    public void Pagination_ShouldClampPageSizeToMin()
    {
        var p = new Pagination(page: 1, pageSize: 0);
        p.PageSize.Should().Be(1, "PageSize must be at least 1~ ✨");
    }

    [Fact]
    public void Pagination_Skip_ShouldComputeCorrectly()
    {
        var p = new Pagination(page: 3, pageSize: 10);
        p.Skip.Should().Be(20);
    }

    [Fact]
    public void Pagination_Default_ShouldBePage1Size50()
    {
        var p = Pagination.Default;
        p.Page.Should().Be(1);
        p.PageSize.Should().Be(Pagination.DefaultPageSize);
    }

    #endregion

    #region PersistenceConfiguration Tests ⚙️

    [Fact]
    public void PersistenceConfiguration_Validate_ShouldThrowWhenProviderNameEmpty()
    {
        var config = new PersistenceConfiguration { ProviderName = "" };
        var act = () => config.Validate();
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PersistenceConfiguration_Validate_ShouldNotThrowWhenValid()
    {
        var config = new PersistenceConfiguration { ProviderName = "postgres" };
        var act = () => config.Validate();
        act.Should().NotThrow();
    }

    #endregion

    #region HealthCheckResult Tests 🏥

    [Fact]
    public void HealthCheckResult_Healthy_ShouldHaveNullError()
    {
        var result = new HealthCheckResult(
            IsHealthy: true,
            ProviderName: "test",
            Latency: TimeSpan.FromMilliseconds(5));

        result.IsHealthy.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void HealthCheckResult_Unhealthy_ShouldHaveError()
    {
        var result = new HealthCheckResult(
            IsHealthy: false,
            ProviderName: "test",
            Latency: TimeSpan.FromMilliseconds(100),
            ErrorMessage: "Connection refused~ 💔");

        result.IsHealthy.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Connection refused");
    }

    #endregion

    #region VariableEntry Null Semantics Tests 💾

    [Fact]
    public void VariableEntry_WithNullValue_ShouldBeDistinctFromNull()
    {
        // VariableEntry with Value = null means "exists, value is null"
        var entry = new VariableEntry(
            VariableScope.Global, "x", Value: null,
            "null", Version: 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        entry.Should().NotBeNull("the entry itself exists~ ✨");
        entry.Value.Should().BeNull("the VALUE is null, but the entry is not~ 💖");
    }

    #endregion

    #region CompositePersistenceProvider Tests 🔀

    [Fact]
    public void Composite_ShouldRouteWorkflowsToConfiguredProvider()
    {
        var workflowRepo = new Mock<IWorkflowRepository>().Object;
        var execRepo = new Mock<IExecutionHistoryRepository>().Object;
        var varStore = new Mock<IVariableStore>().Object;

        var wfProvider = CreateMockProvider("postgres", workflowRepo, execRepo, varStore);
        var varsProvider = CreateMockProvider("nats", workflowRepo, execRepo, varStore);

        var composite = new CompositePersistenceProvider(wfProvider, wfProvider, varsProvider);

        composite.Workflows.Should().BeSameAs(wfProvider.Workflows);
        composite.Variables.Should().BeSameAs(varsProvider.Variables);
    }

    [Fact]
    public async Task Composite_HealthCheck_ShouldAggregateResults()
    {
        var healthy = CreateMockProvider("pg", isHealthy: true);
        var unhealthy = CreateMockProvider("nats", isHealthy: false, error: "down~ 💔");

        var composite = new CompositePersistenceProvider(healthy, healthy, unhealthy);
        var result = await composite.HealthCheckAsync();

        result.IsHealthy.Should().BeFalse("one sub-provider is unhealthy~ 💔");
        result.ProviderName.Should().Be("composite");
        result.Details.Should().ContainKey("nats.error");
    }

    [Fact]
    public async Task Composite_InitializeAsync_ShouldInitAllUniqueProviders()
    {
        var initCount = 0;
        var mock1 = new Mock<IPersistenceProvider>();
        mock1.Setup(p => p.ProviderName).Returns("a");
        mock1.Setup(p => p.Workflows).Returns(new Mock<IWorkflowRepository>().Object);
        mock1.Setup(p => p.ExecutionHistory).Returns(new Mock<IExecutionHistoryRepository>().Object);
        mock1.Setup(p => p.Variables).Returns(new Mock<IVariableStore>().Object);
        mock1.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref initCount))
            .Returns(Task.CompletedTask);
        mock1.Setup(p => p.IsInitialized).Returns(true);

        // Same provider for all roles → should only init ONCE
        var provider = mock1.Object;
        var composite = new CompositePersistenceProvider(provider, provider, provider);
        await composite.InitializeAsync();

        initCount.Should().Be(1, "same instance used for all roles should only init once~ ✨");
        composite.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void Composite_ProviderName_ShouldBeComposite()
    {
        var p = CreateMockProvider("test");
        var composite = new CompositePersistenceProvider(p, p, p);
        composite.ProviderName.Should().Be("composite");
    }

    #endregion

    #region ExecutionRecord Tests 📊

    [Fact]
    public void ExecutionRecord_ShouldRoundTripViaJson()
    {
        var record = new ExecutionRecord(
            ExecutionId: Guid.NewGuid(),
            WorkflowId: Guid.NewGuid(),
            State: ExecutionState.Completed,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt: DateTimeOffset.UtcNow,
            Error: null,
            TriggeredBy: "test");

        var json = JsonSerializer.Serialize(record);
        var deserialized = JsonSerializer.Deserialize<ExecutionRecord>(json);

        deserialized.Should().NotBeNull();
        deserialized!.ExecutionId.Should().Be(record.ExecutionId);
        deserialized.State.Should().Be(ExecutionState.Completed);
    }

    #endregion

    #region Helpers 🛠️

    private static IPersistenceProvider CreateMockProvider(
        string name,
        IWorkflowRepository? workflows = null,
        IExecutionHistoryRepository? execHistory = null,
        IVariableStore? variables = null,
        bool isHealthy = true,
        string? error = null)
    {
        var mock = new Mock<IPersistenceProvider>();
        mock.Setup(p => p.ProviderName).Returns(name);
        mock.Setup(p => p.IsInitialized).Returns(true);
        mock.Setup(p => p.Workflows).Returns(workflows ?? new Mock<IWorkflowRepository>().Object);
        mock.Setup(p => p.ExecutionHistory).Returns(execHistory ?? new Mock<IExecutionHistoryRepository>().Object);
        mock.Setup(p => p.Variables).Returns(variables ?? new Mock<IVariableStore>().Object);
        mock.Setup(p => p.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(p => p.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthCheckResult(isHealthy, name, TimeSpan.FromMilliseconds(1), error));
        return mock.Object;
    }

    #endregion
}

