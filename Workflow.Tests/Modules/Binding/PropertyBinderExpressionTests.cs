// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules.Binding;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Services;
using Workflow.Modules.Binding;
using Xunit;

/// <summary>
/// 🧮 Phase 3.1.7 — Tests for inline expression evaluation in <see cref="PropertyBinder"/>.
/// Expressions like <c>{{Variable.Count &gt; 5}}</c> are evaluated via the injected
/// <see cref="Workflow.Core.Abstractions.IExpressionEvaluator"/> (Jint), while pure
/// <c>{{Variable.X}}</c>/<c>{{NodeId.Output}}</c> references keep their fast path~ ✨💖
/// </summary>
public class PropertyBinderExpressionTests
{
    private readonly PropertyBinder _binder = new(
        new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance));

    private static PropertyBindingContext ContextWith(
        IReadOnlyDictionary<string, object?>? variables = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? nodeOutputs = null)
        => new(
            variables ?? new Dictionary<string, object?>(),
            nodeOutputs ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>());

    /// <summary>➕ A pure arithmetic expression evaluates and converts to the port type.</summary>
    [Fact]
    public void Arithmetic_Evaluates()
    {
        var raw = new Dictionary<string, object?> { ["value"] = "{{1 + 2 * 3}}" };
        var schema = Arr.create(PortDefinition.Create<int>("value"));

        var result = _binder.BindProperties(raw, schema, PropertyBindingContext.Empty);

        result.Success.Should().BeTrue();
        result.BoundValues["value"].Should().Be(7);
    }

    /// <summary>🔎 A comparison expression using a variable evaluates to bool.</summary>
    [Fact]
    public void Comparison_Evaluates()
    {
        var raw = new Dictionary<string, object?> { ["ok"] = "{{Variable.Count > 5}}" };
        var schema = Arr.create(PortDefinition.Create<bool>("ok"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["Count"] = 10 });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["ok"].Should().Be(true);
    }

    /// <summary>🔗 Logical operators combine variable references.</summary>
    [Fact]
    public void Logical_Evaluates()
    {
        var raw = new Dictionary<string, object?> { ["ok"] = "{{Variable.A && !Variable.B}}" };
        var schema = Arr.create(PortDefinition.Create<bool>("ok"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["A"] = true, ["B"] = false });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["ok"].Should().Be(true);
    }

    /// <summary>🧵 String concatenation with a variable produces a string.</summary>
    [Fact]
    public void StringConcat_Evaluates()
    {
        var raw = new Dictionary<string, object?> { ["greeting"] = "{{Variable.Name + '!'}}" };
        var schema = Arr.create(PortDefinition.Create<string>("greeting"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["Name"] = "Ami" });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["greeting"].Should().Be("Ami!");
    }

    /// <summary>💾 A variable reference embedded in a larger expression resolves.</summary>
    [Fact]
    public void VariableReference_InExpression_Resolves()
    {
        var raw = new Dictionary<string, object?> { ["total"] = "{{Variable.Price * Variable.Qty}}" };
        var schema = Arr.create(PortDefinition.Create<int>("total"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["Price"] = 3, ["Qty"] = 4 });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["total"].Should().Be(12);
    }

    /// <summary>📤 A node-output reference embedded in an expression resolves.</summary>
    [Fact]
    public void NodeOutput_InExpression_Resolves()
    {
        var raw = new Dictionary<string, object?> { ["doubled"] = "{{orderNode.total * 2}}" };
        var schema = Arr.create(PortDefinition.Create<int>("doubled"));
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["orderNode"] = new Dictionary<string, object?> { ["total"] = 21 },
        };
        var ctx = ContextWith(nodeOutputs: nodeOutputs);

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["doubled"].Should().Be(42);
    }

    /// <summary>🎯 A whole-template expression preserves the raw evaluated type (bool, not string).</summary>
    [Fact]
    public void WholeTemplate_PreservesType()
    {
        var raw = new Dictionary<string, object?> { ["flag"] = "{{Variable.Count >= 5}}" };
        var schema = Arr.create(PortDefinition.Create<object>("flag"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["Count"] = 7 });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["flag"].Should().BeOfType<bool>();
        result.BoundValues["flag"].Should().Be(true);
    }

    /// <summary>📝 A template with text around an expression interpolates to a string.</summary>
    [Fact]
    public void MixedTemplate_Interpolates()
    {
        var raw = new Dictionary<string, object?> { ["msg"] = "Total is {{Variable.Count + 1}}!" };
        var schema = Arr.create(PortDefinition.Create<string>("msg"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["Count"] = 41 });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["msg"].Should().Be("Total is 42!");
    }

    /// <summary>💥 A syntactically invalid expression produces a binding error (never silent null).</summary>
    [Fact]
    public void InvalidExpression_BindingError()
    {
        var raw = new Dictionary<string, object?> { ["value"] = "{{1 +* 2}}" };
        var schema = Arr.create(PortDefinition.Create<int>("value"));

        var result = _binder.BindProperties(raw, schema, PropertyBindingContext.Empty);

        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().Contain(e => e.Contains("1 +* 2"));
    }

    /// <summary>⏰ An expression that never terminates is aborted by the evaluator timeout.</summary>
    [Fact]
    public void ExpensiveExpression_TimesOut()
    {
        var raw = new Dictionary<string, object?>
        {
            ["value"] = "{{(function(){ while(true){} return 1; })()}}",
        };
        var schema = Arr.create(PortDefinition.Create<int>("value"));

        var result = _binder.BindProperties(raw, schema, PropertyBindingContext.Empty);

        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().NotBeEmpty();
    }

    /// <summary>♻️ Built-in globals like Math are NOT clobbered by reference rewriting.</summary>
    [Fact]
    public void BuiltinGlobals_NotRewrittenAsReferences()
    {
        var raw = new Dictionary<string, object?> { ["max"] = "{{Math.max(Variable.A, Variable.B)}}" };
        var schema = Arr.create(PortDefinition.Create<int>("max"));
        var ctx = ContextWith(new Dictionary<string, object?> { ["A"] = 3, ["B"] = 9 });

        var result = _binder.BindProperties(raw, schema, ctx);

        result.Success.Should().BeTrue();
        result.BoundValues["max"].Should().Be(9);
    }

    /// <summary>🛡️ With expressions disabled, a non-reference template stays a binding error (pre-3.1 behavior).</summary>
    [Fact]
    public void ExpressionsDisabled_NonReferenceTemplate_Errors()
    {
        var binder = new PropertyBinder(
            new JintExpressionEvaluator(NullLogger<JintExpressionEvaluator>.Instance),
            enableExpressions: false);
        var raw = new Dictionary<string, object?> { ["value"] = "{{1 + 2}}" };
        var schema = Arr.create(PortDefinition.Create<int>("value"));

        var result = binder.BindProperties(raw, schema, PropertyBindingContext.Empty);

        result.Success.Should().BeFalse();
    }
}
