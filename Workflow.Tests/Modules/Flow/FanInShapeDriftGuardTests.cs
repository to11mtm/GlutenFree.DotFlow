// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules.Flow;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🛡️ Input-shape hinting drift guard — the designer's <c>FanInShape</c> (Workflow.UI.Client)
/// predicts FanIn result shapes at design time by <b>mirroring</b> this module's semantics. These
/// fixtures are duplicated in <c>Workflow.Tests.UI\State\InputShapeHintingTests.cs</c>: three
/// branches from ports <c>body</c>/<c>body</c>/<c>result</c> (nodes h1/h2/s1) must produce named
/// keys <c>h1.body</c>, <c>h2.body</c>, <c>result</c> on BOTH sides. If either implementation's
/// naming or merge-precedence rule changes, exactly one side breaks and the drift is caught~ ✨.
/// </summary>
public sealed class FanInShapeDriftGuardTests
{
    private readonly FanInModule _module = new();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static ModuleExecutionContext Context(
        List<Dictionary<string, object?>> branches,
        List<Dictionary<string, object?>> meta,
        string mode)
        => new()
        {
            Inputs = new Dictionary<string, object?>
            {
                ["__incomingBranches__"] = branches,
                ["__incomingBranchMeta__"] = meta,
            },
            Properties = new Dictionary<string, object?> { ["mode"] = mode },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "fanin-node",
        };

    // Shared fixture: h1.body, h2.body (colliding port name), s1.result (unique).
    private static (List<Dictionary<string, object?>> Branches, List<Dictionary<string, object?>> Meta) Fixture()
        => (
            new List<Dictionary<string, object?>>
            {
                new() { ["body"] = "from-h1" },
                new() { ["body"] = "from-h2" },
                new() { ["result"] = "from-s1" },
            },
            new List<Dictionary<string, object?>>
            {
                new() { ["sourceNodeId"] = "h1", ["sourcePortName"] = "body" },
                new() { ["sourceNodeId"] = "h2", ["sourcePortName"] = "body" },
                new() { ["sourceNodeId"] = "s1", ["sourcePortName"] = "result" },
            });

    [Fact]
    public async Task NamedKeys_MatchTheClientPrediction()
    {
        var (branches, meta) = Fixture();

        var result = await _module.ExecuteAsync(Context(branches, meta, "named"));

        result.Success.Should().BeTrue();
        var named = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;

        // ⚠ Must equal FanInShape.NamedKeys' prediction for the same branches (client-side test).
        named.Keys.Should().BeEquivalentTo(["h1.body", "h2.body", "result"]);
        named["h1.body"].Should().Be("from-h1");
        named["h2.body"].Should().Be("from-h2");
        named["result"].Should().Be("from-s1");
    }

    [Fact]
    public async Task MergePrecedence_MatchesTheClientPrediction()
    {
        var (branches, meta) = Fixture();

        var result = await _module.ExecuteAsync(Context(branches, meta, "merge"));

        result.Success.Should().BeTrue();
        var merged = result.Outputs["result"].Should().BeAssignableTo<Dictionary<string, object?>>().Which;

        // ⚠ Must match FanInShape.MergeKeys: 'body' won by branch index 1 (last writer), 'result' by 2.
        merged["body"].Should().Be("from-h2");
        merged["result"].Should().Be("from-s1");
    }
}
