// <copyright file="TransformInfrastructureTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Abstractions;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform.Internal;
using Xunit;

/// <summary>
/// 🔄 Phase 2.6.a.0 — tests for the transform infra (normalizer, dot-path, expression bridge)~ ✨.
/// </summary>
public sealed class TransformInfrastructureTests
{
    private static ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        sc.AddLogging();

        // Wire the 2.2.5 evaluators (host normally does this)~ 🧮
        sc.AddSingleton<IExpressionEvaluator, JintExpressionEvaluator>();
        sc.AddKeyedSingleton<IExpressionEvaluator, DynamicExpressoEvaluator>("csharp");
        return sc.BuildServiceProvider();
    }

    private static ModuleExecutionContext Context(IServiceProvider services, Dictionary<string, object?>? variables = null)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = new Dictionary<string, object?>(),
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "transform-node",
        };

    [Fact]
    public void Normalizer_ClrDictAndList_PassThrough()
    {
        var input = new List<object?> { new Dictionary<string, object?> { ["a"] = 1 } };
        var result = TransformDataNormalizer.Normalize(input);
        result.Should().BeAssignableTo<List<object?>>();
    }

    [Fact]
    public void Normalizer_JsonNode_ConvertsToClrShape()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse("{\"a\":1,\"b\":[2,3]}");
        var result = TransformDataNormalizer.Normalize(node);
        result.Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>();
    }

    [Fact]
    public void Normalizer_AsRows_NonRecordItem_FriendlyError()
    {
        var ok = TransformDataNormalizer.AsRows(new List<object?> { 1, 2 }, out _, out var error);
        ok.Should().BeFalse();
        error.Should().Contain("not a record");
    }

    [Fact]
    public void Normalizer_AsRows_ArrayOfRecords_Works()
    {
        var data = new List<object?>
        {
            new Dictionary<string, object?> { ["name"] = "Ada" },
            new Dictionary<string, object?> { ["name"] = "Grace" },
        };
        TransformDataNormalizer.AsRows(data, out var rows, out _).Should().BeTrue();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public void DotPath_NestedResolve_Works()
    {
        var root = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?> { ["address"] = new Dictionary<string, object?> { ["city"] = "Paris" } },
        };
        DotPath.Resolve(root, "user.address.city", out var found).Should().Be("Paris");
        found.Should().BeTrue();
    }

    [Fact]
    public void DotPath_MissingSegment_NotFound()
    {
        var root = new Dictionary<string, object?> { ["a"] = 1 };
        DotPath.Resolve(root, "a.b.c", out var found);
        found.Should().BeFalse();
    }

    [Fact]
    public async Task ItemEvaluator_JsPredicate_SeesItemAndIndex()
    {
        using var services = BuildServices();
        var ctx = Context(services);
        ItemExpressionEvaluator.TryResolve(ctx, null, out var bridge, out _).Should().BeTrue();

        var item = new Dictionary<string, object?> { ["price"] = 20L };
        var scope = ItemExpressionEvaluator.Scope(ctx, item, 3);

        (await bridge.EvalPredicateAsync("item.price > 10 && index === 3", scope, 3, CancellationToken.None))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ItemEvaluator_CsharpLanguageKey_UsesKeyedEvaluator()
    {
        using var services = BuildServices();
        var ctx = Context(services);
        ItemExpressionEvaluator.TryResolve(ctx, "csharp", out var bridge, out _).Should().BeTrue();

        var scope = ItemExpressionEvaluator.Scope(ctx, null, 0, new Dictionary<string, object?> { ["x"] = 5 });
        (await bridge.EvalValueAsync("x + 1", scope, 0, CancellationToken.None)).Should().Be(6);
    }

    [Fact]
    public async Task ItemEvaluator_WorkflowVariables_Visible()
    {
        using var services = BuildServices();
        var ctx = Context(services, new Dictionary<string, object?> { ["threshold"] = 100L });
        ItemExpressionEvaluator.TryResolve(ctx, null, out var bridge, out _);

        var item = new Dictionary<string, object?> { ["v"] = 150L };
        var scope = ItemExpressionEvaluator.Scope(ctx, item, 0);
        (await bridge.EvalPredicateAsync("item.v > threshold", scope, 0, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public void ItemEvaluator_MissingRegistration_FriendlyFail()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        using var services = sc.BuildServiceProvider();
        var ctx = Context(services);

        ItemExpressionEvaluator.TryResolve(ctx, null, out _, out var failure).Should().BeFalse();
        failure!.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ItemEvaluator_ExpressionError_CarriesItemIndex()
    {
        using var services = BuildServices();
        var ctx = Context(services);
        ItemExpressionEvaluator.TryResolve(ctx, null, out var bridge, out _);
        var scope = ItemExpressionEvaluator.Scope(ctx, null, 7);

        var act = async () => await bridge.EvalValueAsync("this is not valid !!!", scope, 7, CancellationToken.None);
        (await act.Should().ThrowAsync<TransformModuleException>()).Which.ItemIndex.Should().Be(7);
    }
}
