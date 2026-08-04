// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules.Transform;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Transform;
using Xunit;

/// <summary>
/// 🛡️ Split preview drift guard (V4) — the designer's <c>SplitPreview</c> (Workflow.UI.Client)
/// replays this module's semantics client-side. These fixtures are duplicated in
/// <c>Workflow.Tests.UI\State\SplitPreviewTests.cs</c>: object <c>{ Foo:1, Bar:"hello", Baz:3 }</c>
/// with keys <c>[Foo, Bar, Qux]</c> and rest port <c>rest</c> must yield <c>Foo=1</c>,
/// <c>Bar="hello"</c>, <c>Qux=null</c> (missing key), <c>rest={ Baz:3 }</c> on BOTH sides. If the
/// module's rules change, update the client mirror and both fixture sets together~ ✨.
/// </summary>
public sealed class SplitPreviewDriftGuardTests
{
    private readonly SplitModule _module = new();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public async Task SplitModule_MatchesTheClientPreviewFixture()
    {
        var ctx = new ModuleExecutionContext
        {
            Inputs = new Dictionary<string, object?>
            {
                ["value"] = """{ "Foo": 1, "Bar": "hello", "Baz": 3 }""",
            },
            Properties = new Dictionary<string, object?>
            {
                ["keys"] = """["Foo", "Bar", "Qux"]""",
                ["restPort"] = "rest",
            },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "split-node",
        };

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();

        // ⚠ Must match SplitPreview.Compute for the same fixture (client-side test).
        Convert.ToInt32(result.Outputs["Foo"]).Should().Be(1);
        result.Outputs["Bar"].Should().Be("hello");
        result.Outputs["Qux"].Should().BeNull("missing keys emit null, never skip the port");
        var rest = result.Outputs["rest"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Which;
        rest.Keys.Should().BeEquivalentTo(["Baz"]);
        Convert.ToInt32(rest["Baz"]).Should().Be(3);
    }
}
