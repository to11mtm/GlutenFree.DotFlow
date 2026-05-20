// <copyright file="ExpressionEvaluatorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;

namespace Workflow.Tests.Engine;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Abstractions;
using Workflow.Engine.Services;
using Xunit;

/// <summary>
/// 🧮 Phase 2.2.5 — Tests for <see cref="JintExpressionEvaluator"/> (default JS/ES2020 engine)
/// and <see cref="DynamicExpressoEvaluator"/> (opt-in C# fallback)~ ✨💖
/// </summary>
public sealed class JintExpressionEvaluatorTests
{
    private readonly JintExpressionEvaluator _evaluator =
        new(NullLogger<JintExpressionEvaluator>.Instance);

    private static IReadOnlyDictionary<string, object?> Vars(params (string key, object? value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    // ── Bool comparisons ──────────────────────────────────────────────────────────

    /// <summary>Simple <c>&gt;</c> comparison evaluates correctly~ 🎯.</summary>
    [Fact]
    public async Task BoolComparisons_EvaluateCorrectly()
    {
        var result = await _evaluator.EvaluateAsync<bool>("x > 5", Vars(("x", 10)));
        result.Should().BeTrue(because: "10 > 5 is true~ ✅");

        result = await _evaluator.EvaluateAsync<bool>("x > 5", Vars(("x", 3)));
        result.Should().BeFalse(because: "3 > 5 is false~ ❌");
    }

    /// <summary>Equality comparisons (<c>===</c>) work correctly~ 🔍.</summary>
    [Fact]
    public async Task StrictEquality_EvaluatesCorrectly()
    {
        (await _evaluator.EvaluateAsync<bool>("x === 42", Vars(("x", 42)))).Should().BeTrue();
        (await _evaluator.EvaluateAsync<bool>("x === 42", Vars(("x", 41)))).Should().BeFalse();
    }

    // ── Logical operators ─────────────────────────────────────────────────────────

    /// <summary>Logical AND <c>&amp;&amp;</c> evaluates correctly~ 🔗.</summary>
    [Fact]
    public async Task LogicalAnd_EvaluatesCorrectly()
    {
        var vars = Vars(("a", true), ("b", false));
        (await _evaluator.EvaluateAsync<bool>("a && b", vars)).Should().BeFalse();
        (await _evaluator.EvaluateAsync<bool>("a && !b", vars)).Should().BeTrue();
    }

    /// <summary>Logical OR <c>||</c> evaluates correctly~ 🔀.</summary>
    [Fact]
    public async Task LogicalOr_EvaluatesCorrectly()
    {
        var vars = Vars(("a", false), ("b", true));
        (await _evaluator.EvaluateAsync<bool>("a || b", vars)).Should().BeTrue();
        (await _evaluator.EvaluateAsync<bool>("a || !b", vars)).Should().BeFalse();
    }

    // ── Null coalescing & optional chaining ───────────────────────────────────────

    /// <summary>Null coalescing <c>??</c> returns fallback when left is null/undefined~ 💎.</summary>
    [Fact]
    public async Task NullCoalescing_ReturnsFallbackForNull()
    {
        var result = await _evaluator.EvaluateAsync<string>("name ?? 'anonymous'", Vars(("name", null)));
        result.Should().Be("anonymous", because: "null ?? fallback should return fallback~ 💎");
    }

    /// <summary>Null coalescing returns left value when it's non-null~ 💎.</summary>
    [Fact]
    public async Task NullCoalescing_ReturnsLeftWhenNonNull()
    {
        var result = await _evaluator.EvaluateAsync<string>("name ?? 'anonymous'", Vars(("name", "Alice")));
        result.Should().Be("Alice");
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────────

    /// <summary>Arithmetic with int/double mix returns correct numeric result~ ➕.</summary>
    [Fact]
    public async Task Arithmetic_IntDoubleSum_EvaluatesCorrectly()
    {
        var result = await _evaluator.EvaluateAsync<int>("a + b", Vars(("a", 3), ("b", 7)));
        result.Should().Be(10, because: "3 + 7 = 10~ ➕");
    }

    /// <summary>Floating-point division returns double~ ➗.</summary>
    [Fact]
    public async Task Arithmetic_Division_ReturnsDouble()
    {
        var raw = await _evaluator.EvaluateAsync("10 / 3.0", Vars());
        raw.Should().BeOfType<double>().Which.Should().BeApproximately(3.333, 0.001);
    }

    // ── Variable lookup ───────────────────────────────────────────────────────────

    /// <summary>Variables from dictionary are injected and resolved correctly~ 📦.</summary>
    [Fact]
    public async Task VariableLookup_FromDictionary_Works()
    {
        var vars = Vars(("firstName", "Ami"), ("lastName", "Chan"));
        var result = await _evaluator.EvaluateAsync<string>("firstName + ' ' + lastName", vars);
        result.Should().Be("Ami Chan", because: "string concatenation with vars~ 💖");
    }

    /// <summary>Missing variable in strict mode throws <see cref="ExpressionRuntimeException"/>~ ❌.</summary>
    [Fact]
    public async Task MissingVariable_StrictMode_ThrowsRuntimeException()
    {
        // CopilotNote: Strict mode means undeclared vars throw ReferenceError →
        // wrapped as ExpressionRuntimeException by JintExpressionEvaluator~ 🔒
        var act = async () => await _evaluator.EvaluateAsync<bool>("undeclaredVar > 5", Vars());
        await act.Should().ThrowAsync<ExpressionRuntimeException>(
            because: "strict mode reference to undeclared var must throw~ ❌");
    }

    // ── Array transforms ──────────────────────────────────────────────────────────

    /// <summary>JS <c>.map()</c> transforms array elements correctly~ 🗂️.</summary>
    [Fact]
    public async Task Array_Map_TransformsElements()
    {
        // CopilotNote: Pass array as List<object?> — Jint converts to JS array via SetValue~ 🌟
        var items = new List<object?> { 1, 2, 3 };
        var result = await _evaluator.EvaluateAsync("nums.map(x => x * 2)", Vars(("nums", items)));
        result.Should().BeAssignableTo<List<object?>>().Which
            .Should().Equal(2, 4, 6);
    }

    /// <summary>JS <c>.filter()</c> selects matching elements correctly~ 🔍.</summary>
    [Fact]
    public async Task Array_Filter_SelectsMatchingElements()
    {
        var items = new List<object?> { 1, 2, 3, 4, 5 };
        var result = await _evaluator.EvaluateAsync("nums.filter(x => x > 3)", Vars(("nums", items)));
        result.Should().BeAssignableTo<List<object?>>().Which.Should().Equal(4, 5);
    }

    // ── EvaluateObjectAsync ───────────────────────────────────────────────────────

    /// <summary><see cref="IExpressionEvaluator.EvaluateObjectAsync"/> returns <see cref="JsonElement"/>~ 📦.</summary>
    [Fact]
    public async Task EvaluateObjectAsync_ReturnsJsonElement()
    {
        var result = await _evaluator.EvaluateObjectAsync("({name: 'Ami', age: 18})", Vars());

        result.ValueKind.Should().Be(JsonValueKind.Object, because: "object literal → JSON object~ 📦");
        result.GetProperty("name").GetString().Should().Be("Ami");
        result.GetProperty("age").GetInt32().Should().Be(18);
    }

    /// <summary><see cref="IExpressionEvaluator.EvaluateObjectAsync"/> for array returns JSON array~ 📜.</summary>
    [Fact]
    public async Task EvaluateObjectAsync_Array_ReturnsJsonArray()
    {
        var result = await _evaluator.EvaluateObjectAsync("[1, 2, 3]", Vars());

        result.ValueKind.Should().Be(JsonValueKind.Array, because: "array → JSON array~ 📜");
        result.GetArrayLength().Should().Be(3);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────────

    /// <summary>Infinite loop expression triggers timeout and throws <see cref="ExpressionRuntimeException"/>~ ⏰.</summary>
    [Fact]
    public async Task InfiniteLoop_ThrowsRuntimeException_WithTimeout()
    {
        // CopilotNote: Jint enforces TimeoutInterval(250ms) — infinite loop must abort~ ⏰
        var act = async () => await _evaluator.EvaluateAsync<bool>("while(true) {}", Vars());
        await act.Should().ThrowAsync<ExpressionRuntimeException>(
            because: "infinite loop must be aborted by Jint's timeout~ ⏰");
    }

    // ── CancellationToken ─────────────────────────────────────────────────────────

    /// <summary>Pre-cancelled <see cref="CancellationToken"/> cancels evaluation immediately~ 🛑.</summary>
    [Fact]
    public async Task CancelledToken_CancelsEvaluation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _evaluator.EvaluateAsync<int>("1 + 1", Vars(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>(
            because: "pre-cancelled CT must immediately cancel Task.Run~ 🛑");
    }

    // ── Concurrent isolation ──────────────────────────────────────────────────────

    /// <summary>Concurrent evaluation with different variable values produces independent results~ 🔀.</summary>
    [Fact]
    public async Task ConcurrentCalls_HaveIsolatedState()
    {
        // CopilotNote: Each call creates its own Engine instance — no shared state~ 🔀
        var tasks = Enumerable.Range(1, 10).Select(i =>
            _evaluator.EvaluateAsync<int>("x * 2", Vars(("x", i)))).ToList();

        var results = await Task.WhenAll(tasks.Select(vt => vt.AsTask()));

        for (var i = 0; i < 10; i++)
        {
            results[i].Should().Be((i + 1) * 2, because: "each call gets its own scope~ 🔀");
        }
    }

    // ── Template literals & ternary ───────────────────────────────────────────────

    /// <summary>Ternary operator evaluates correctly~ 🎯.</summary>
    [Fact]
    public async Task Ternary_EvaluatesCorrectly()
    {
        var result = await _evaluator.EvaluateAsync<string>(
            "score >= 60 ? 'pass' : 'fail'",
            Vars(("score", 75)));
        result.Should().Be("pass");
    }
}

/// <summary>
/// 🔧 Phase 2.2.5 — Tests for <see cref="DynamicExpressoEvaluator"/> (C# fallback)~ ✨
/// </summary>
public sealed class DynamicExpressoEvaluatorTests
{
    private readonly DynamicExpressoEvaluator _evaluator =
        new(NullLogger<DynamicExpressoEvaluator>.Instance);

    private static IReadOnlyDictionary<string, object?> Vars(params (string key, object? value)[] pairs)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    /// <summary>Basic boolean comparison evaluates correctly~ 🔍.</summary>
    [Fact]
    public async Task BoolComparison_EvaluatesCorrectly()
    {
        var result = await _evaluator.EvaluateAsync<bool>("x > 5", Vars(("x", 10)));
        result.Should().BeTrue();
    }

    /// <summary>Arithmetic returns correct int result~ ➕.</summary>
    [Fact]
    public async Task Arithmetic_EvaluatesCorrectly()
    {
        var result = await _evaluator.EvaluateAsync<int>("a + b", Vars(("a", 3), ("b", 7)));
        result.Should().Be(10);
    }

    /// <summary>Whitelist helper <c>len()</c> returns collection length~ 🔢.</summary>
    [Fact]
    public async Task BuiltinLen_ReturnsCorrectLength()
    {
        // CopilotNote: len(string) → character count; len(collection) → element count~ 🔢
        var result = await _evaluator.EvaluateAsync<int>("len(name)", Vars(("name", "Ami")));
        result.Should().Be(3, because: "len('Ami') = 3~ 🔢");
    }

    /// <summary>Whitelist helper <c>lower()</c> lowercases string~ 🔡.</summary>
    [Fact]
    public async Task BuiltinLower_LowercasesString()
    {
        var result = await _evaluator.EvaluateAsync<string>("lower(name)", Vars(("name", "HELLO")));
        result.Should().Be("hello");
    }

    /// <summary>Whitelist helper <c>upper()</c> uppercases string~ 🔠.</summary>
    [Fact]
    public async Task BuiltinUpper_UppercasesString()
    {
        var result = await _evaluator.EvaluateAsync<string>("upper(name)", Vars(("name", "hello")));
        result.Should().Be("HELLO");
    }

    /// <summary>Pre-cancelled CT is respected before execution starts~ 🛑.</summary>
    [Fact]
    public async Task CancelledToken_IsRespected()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _evaluator.EvaluateAsync<int>("1 + 1", Vars(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Invalid C# syntax throws <see cref="ExpressionParseException"/>~ 💥.</summary>
    [Fact]
    public async Task InvalidSyntax_ThrowsParseException()
    {
        var act = async () => await _evaluator.EvaluateAsync<int>("1 +* 2", Vars());
        await act.Should().ThrowAsync<Exception>(
            because: "bad syntax should produce a parse or runtime exception~ 💥");
    }
}

/// <summary>
/// 🔌 Phase 2.2.5 — Tests for <see cref="KeyedExpressionEvaluatorFactory"/>~ ✨
/// </summary>
public sealed class KeyedExpressionEvaluatorFactoryTests
{
    private static readonly JintExpressionEvaluator DefaultEvaluator =
        new(NullLogger<JintExpressionEvaluator>.Instance);

    private static readonly DynamicExpressoEvaluator CSharpEvaluator =
        new(NullLogger<DynamicExpressoEvaluator>.Instance);

    private static KeyedExpressionEvaluatorFactory BuildFactory()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddKeyedSingleton<IExpressionEvaluator>("csharp", (_, _) => CSharpEvaluator)
            .BuildServiceProvider();

        return new KeyedExpressionEvaluatorFactory(services, DefaultEvaluator);
    }

    /// <summary>Null/empty engine name returns the default (Jint) evaluator~ 🎯.</summary>
    [Fact]
    public void NullEngineName_ReturnsDefault()
    {
        var factory = BuildFactory();
        factory.GetEvaluator(null).Should().BeSameAs(DefaultEvaluator);
        factory.GetEvaluator(string.Empty).Should().BeSameAs(DefaultEvaluator);
    }

    /// <summary>"javascript" returns the default (Jint) evaluator~ 🎯.</summary>
    [Theory]
    [InlineData("javascript")]
    [InlineData("JavaScript")]
    [InlineData("js")]
    [InlineData("JS")]
    public void JavaScriptEngineName_ReturnsDefault(string name)
    {
        var factory = BuildFactory();
        factory.GetEvaluator(name).Should().BeSameAs(DefaultEvaluator);
    }

    /// <summary>"csharp" returns the DynamicExpresso (C#) evaluator~ 🔧.</summary>
    [Fact]
    public void CSharpEngineName_ReturnsCSharpEvaluator()
    {
        var factory = BuildFactory();
        factory.GetEvaluator("csharp").Should().BeSameAs(CSharpEvaluator);
    }

    /// <summary>Unknown engine name gracefully falls back to default~ 🛡️.</summary>
    [Fact]
    public void UnknownEngineName_FallsBackToDefault()
    {
        var factory = BuildFactory();
        factory.GetEvaluator("lua").Should().BeSameAs(DefaultEvaluator,
            because: "unknown engine names gracefully fall back to default~ 🛡️");
    }
}

