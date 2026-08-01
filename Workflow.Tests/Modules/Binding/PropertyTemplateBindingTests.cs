// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Binding;

namespace Workflow.Tests.Modules.Binding;

/// <summary>
/// 🔗 Phase 3.5 (V4) — template expansion in node <b>properties</b>, and the deliberate refusal to
/// expand anything that hasn't opted in.
/// </summary>
/// <remarks>
/// Two behaviours are pinned here. Properties are the designer's whole editing surface, and until
/// V4 the engine never expanded them — so every token the <c>{{x}}</c> picker inserted arrived at
/// the module as literal text. Conversely inputs used to expand <em>unconditionally</em>, which let
/// upstream data reference workflow variables.
/// </remarks>
public class PropertyTemplateBindingTests
{
    private static readonly PropertyBinder Binder = new();

    private static PropertyBindingContext Context(params (string Name, object? Value)[] variables)
        => new(
            variables.ToDictionary(v => v.Name, v => v.Value),
            new Dictionary<string, IReadOnlyDictionary<string, object?>>());

    private static Arr<ModulePropertyDefinition> Schema(params ModulePropertyDefinition[] properties)
        => properties.ToArr();

    private static ModulePropertyDefinition Templated(string name)
        => new(name, name, typeof(string), SupportsTemplates: true);

    private static ModulePropertyDefinition Literal(string name)
        => new(name, name, typeof(string));

    [Fact]
    public void TemplatedProperty_ResolvesVariableReference()
    {
        // The headline fix: an HTTP url built from a variable finally reaches the module resolved.
        var raw = new Dictionary<string, object?> { ["url"] = "{{Variable.host}}/orders" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("url")), Context(("host", "https://api.example.com")));

        result.Success.Should().BeTrue();
        result.BoundValues["url"].Should().Be("https://api.example.com/orders");
    }

    [Fact]
    public void WholeTokenProperty_PreservesTheResolvedType()
    {
        var raw = new Dictionary<string, object?> { ["retries"] = "{{Variable.count}}" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("retries")), Context(("count", 5)));

        result.BoundValues["retries"].Should().Be(5, because: "a whole-token reference keeps its type");
    }

    [Fact]
    public void NonTemplatedProperty_IsLeftExactlyAsAuthored()
    {
        // SQL text, script bodies and connection strings must never be rewritten — values bind
        // through parameters instead (D7).
        const string sql = "SELECT * FROM Orders WHERE Host = {{Variable.host}}";
        var raw = new Dictionary<string, object?> { ["query"] = sql };

        var result = Binder.BindModuleProperties(raw, Schema(Literal("query")), Context(("host", "evil")));

        result.Success.Should().BeTrue();
        result.BoundValues["query"].Should().Be(sql);
    }

    [Fact]
    public void UnresolvableReference_InATemplatedProperty_FailsTheBinding()
    {
        // Q4: fail hard. Silently passing "{{Variable.typo}}" downstream produced bugs that only
        // surfaced much later, and much less legibly.
        var raw = new Dictionary<string, object?> { ["url"] = "{{Variable.typo}}/orders" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("url")), Context(("host", "x")));

        result.Success.Should().BeFalse();
        result.Errors.Count.Should().Be(1);
        result.Errors[0].Should().Contain("typo");
    }

    [Fact]
    public void PropertyErrors_ReadAsPropertiesNotInputs()
    {
        var raw = new Dictionary<string, object?> { ["url"] = "{{Variable.missing}}" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("url")), Context());

        result.Errors[0].Should().StartWith("Property 'url'");
    }

    [Fact]
    public void EscapedBraces_ProduceALiteralAndDoNotResolve()
    {
        // Q12: the escape hatch that makes fail-hard survivable for values that genuinely contain
        // "{{" — a Mustache template being written to a file, say.
        var raw = new Dictionary<string, object?> { ["body"] = @"Hello \{\{name}}" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("body")), Context());

        result.Success.Should().BeTrue();
        result.BoundValues["body"].Should().Be("Hello {{name}}");
    }

    [Fact]
    public void EscapedAndRealReferences_CanCoexist()
    {
        var raw = new Dictionary<string, object?> { ["body"] = @"{{Variable.greeting}} \{\{literal}}" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("body")), Context(("greeting", "hi")));

        result.BoundValues["body"].Should().Be("hi {{literal}}");
    }

    [Fact]
    public void NonStringProperties_PassThroughUntouched()
    {
        var raw = new Dictionary<string, object?> { ["retries"] = 3, ["enabled"] = true };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("retries"), Templated("enabled")), Context());

        result.BoundValues["retries"].Should().Be(3);
        result.BoundValues["enabled"].Should().Be(true);
    }

    [Fact]
    public void PropertiesNotInTheSchema_PassThrough()
    {
        // Reserved designer properties like outputMode aren't in the module schema.
        var raw = new Dictionary<string, object?> { ["outputMode"] = "merged" };

        var result = Binder.BindModuleProperties(raw, Schema(Templated("url")), Context());

        result.BoundValues["outputMode"].Should().Be("merged");
    }

    // ── F9: inputs must not expand unless the port opts in ──────────────────────

    [Fact]
    public void InputCarryingBracesFromUpstreamData_IsPassedThroughUntouched()
    {
        // 🛡️ The regression guard for F9. An input's value is upstream data or a run input, never
        // text an author typed. Expanding it let untrusted content — an HTTP response body, a
        // database row — reference workflow variables, and mangled anything legitimately
        // containing "{{" (a Mustache email template read out of a table, say).
        const string upstreamData = "Dear {{customer.name}}, your order {{Variable.secret}} shipped";
        var raw = new Dictionary<string, object?> { ["body"] = upstreamData };
        var schema = Arr.create(PortDefinition.Create<string>("body"));

        var result = Binder.BindProperties(raw, schema, Context(("secret", "hunter2")));

        result.Success.Should().BeTrue(because: "unresolvable data must not fail the node either");
        result.BoundValues["body"].Should().Be(upstreamData);
        result.BoundValues["body"].As<string>().Should().NotContain("hunter2", because: "data must not be able to read variables");
    }

    [Fact]
    public void InputThatOptsIn_StillResolves()
    {
        var raw = new Dictionary<string, object?> { ["greeting"] = "{{Variable.name}}" };
        var schema = Arr.create(PortDefinition.Create<string>("greeting", supportsTemplates: true));

        var result = Binder.BindProperties(raw, schema, Context(("name", "Ami")));

        result.BoundValues["greeting"].Should().Be("Ami");
    }

    [Fact]
    public void InputThatOptsIn_AlsoHonoursTheEscape()
    {
        var raw = new Dictionary<string, object?> { ["greeting"] = @"\{\{not a token}}" };
        var schema = Arr.create(PortDefinition.Create<string>("greeting", supportsTemplates: true));

        var result = Binder.BindProperties(raw, schema, Context());

        result.BoundValues["greeting"].Should().Be("{{not a token}}");
    }
}
