// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules.Binding;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Binding;
using Xunit;

/// <summary>
/// 🔌 HTTP-input plan D4/D5 — Tests for the <c>{{input}}</c> / <c>{{input.path}}</c> template
/// root: resolves the executing node's own <c>input</c> port from
/// <see cref="PropertyBindingContext.SelfInputs"/>, dot-path traversal below it, reserved-root
/// precedence over a node id named 'input', and inert behaviour without SelfInputs~ ✨💖.
/// </summary>
public class PropertyBinderSelfInputTests
{
    private readonly PropertyBinder _binder = new();

    private static Arr<ModulePropertyDefinition> TextProperty(string name = "url")
        => Arr.create(new ModulePropertyDefinition(
            name, name, typeof(string), "templated~", false, null, PropertyEditorType.Text, null, SupportsTemplates: true));

    private static PropertyBindingContext ContextWithInput(object? inputValue)
        => new(
            new Dictionary<string, object?>(),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(),
            null,
            new Dictionary<string, object?> { ["input"] = inputValue });

    [Fact]
    public void BareInput_ResolvesWholePortValue()
    {
        var raw = new Dictionary<string, object?> { ["url"] = "{{input}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), ContextWithInput("order-42"));

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("order-42");
    }

    [Fact]
    public void DottedInput_TraversesDictionaries()
    {
        var input = new Dictionary<string, object?>
        {
            ["Thing"] = new Dictionary<string, object?> { ["Id"] = 42 },
        };
        var raw = new Dictionary<string, object?> { ["url"] = "https://api/things/{{input.Thing.Id}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), ContextWithInput(input));

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("https://api/things/42");
    }

    [Fact]
    public void DottedInput_TraversesJsonElements()
    {
        var input = JsonDocument.Parse("""{ "Thing": { "Id": "abc" } }""").RootElement.Clone();
        var raw = new Dictionary<string, object?> { ["url"] = "{{input.Thing.Id}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), ContextWithInput(input));

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("abc");
    }

    [Fact]
    public void InputRoot_IsCaseInsensitive()
    {
        var input = new Dictionary<string, object?> { ["id"] = 7 };
        var raw = new Dictionary<string, object?> { ["url"] = "{{Input.id}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), ContextWithInput(input));

        result.Success.Should().BeTrue();

        // A whole-property template keeps the native type (only interpolation stringifies).
        result.BoundValues["url"].Should().Be(7);
    }

    [Fact]
    public void MissingInputValue_FailsWithConnectHint()
    {
        var context = new PropertyBindingContext(
            new Dictionary<string, object?>(),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(),
            null,
            new Dictionary<string, object?>()); // SelfInputs present, but no 'input' key
        var raw = new Dictionary<string, object?> { ["url"] = "{{input}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), context);

        result.Success.Should().BeFalse();
        result.Errors.AsEnumerable().Should().ContainSingle(e => e.Contains("connect a previous step", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithoutSelfInputs_InputRootStaysInert()
    {
        // No SelfInputs on the context (standalone binding) — 'input.x' falls through to the
        // NodeId.Output path and reports the node as missing, exactly as before this feature.
        var raw = new Dictionary<string, object?> { ["url"] = "{{input.x}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), PropertyBindingContext.Empty);

        result.Success.Should().BeFalse();
        result.Errors.AsEnumerable().Should().ContainSingle(e => e.Contains("'input' not found"));
    }

    [Fact]
    public void ReservedRoot_WinsOverNodeNamedInput()
    {
        // D5 — a (legal-but-exotic) node with id 'input' is shadowed by the reserved root.
        var context = new PropertyBindingContext(
            new Dictionary<string, object?>(),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "from-node" },
            },
            null,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?> { ["id"] = "from-self-input" },
            });
        var raw = new Dictionary<string, object?> { ["url"] = "{{input.id}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), context);

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("from-self-input");
    }

    [Fact]
    public void NodeReferences_StillResolve_WhenSelfInputsPresent()
    {
        var context = new PropertyBindingContext(
            new Dictionary<string, object?>(),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>
            {
                ["http-1"] = new Dictionary<string, object?> { ["body"] = "payload" },
            },
            null,
            new Dictionary<string, object?> { ["input"] = "x" });
        var raw = new Dictionary<string, object?> { ["url"] = "{{http-1.body}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), context);

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("payload");
    }

    [Fact]
    public void TraversingMissingPath_Fails()
    {
        var input = new Dictionary<string, object?> { ["Thing"] = null };
        var raw = new Dictionary<string, object?> { ["url"] = "{{input.Thing.Id}}" };

        var result = _binder.BindModuleProperties(raw, TextProperty(), ContextWithInput(input));

        result.Success.Should().BeFalse();
        result.Errors.AsEnumerable().Should().ContainSingle(e => e.Contains("cannot traverse into null"));
    }
}
