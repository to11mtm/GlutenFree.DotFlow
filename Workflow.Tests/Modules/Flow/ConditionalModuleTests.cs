// <copyright file="ConditionalModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Workflow.Core.Abstractions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🔀 Phase 2.2.1 — Tests for <see cref="ConditionalModule"/> (<c>builtin.condition</c>)~
/// Validates bool coercion, string parsing, expression evaluation, diagnostics, and schema~ ✨💖
/// </summary>
public sealed class ConditionalModuleTests
{
    private readonly ConditionalModule _module = new();

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? properties = null,
        Dictionary<string, object?>? variables = null,
        IServiceProvider? services = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = services ?? new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "condition-node",
        };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static IServiceProvider MockEvaluatorServices(bool evaluationResult, string expression = "x > 5")
    {
        var mock = new Mock<IExpressionEvaluator>();
        mock.Setup(e => e.EvaluateAsync<bool>(
                It.Is<string>(s => s == expression),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluationResult);

        var sp = new Mock<IServiceProvider>();
        sp.Setup(s => s.GetService(typeof(IExpressionEvaluator))).Returns(mock.Object);
        return sp.Object;
    }

    // ── Schema & metadata ─────────────────────────────────────────────────────────────

    /// <summary>Module ID, category, and version are set correctly~ 🏷️</summary>
    [Fact]
    public void ConditionalModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.condition");
        _module.Category.Should().Be("Flow Control");
        _module.DisplayName.Should().Be("Conditional Branch");
        _module.Version.Should().Be(new Version(1, 0, 0));
        _module.Icon.Should().Be("🔀");
    }

    /// <summary>Schema declares the three expected output ports~ 📋</summary>
    [Fact]
    public void ConditionalModule_Schema_DeclaresTrueFlaseResultPorts()
    {
        var outputNames = _module.Schema.Outputs.Select(p => p.Name).ToList();
        outputNames.Should().Contain("true", because: "true branch port must be declared for ValidateConnectionPorts~ 🎯");
        outputNames.Should().Contain("false", because: "false branch port must be declared~ 🎯");
        outputNames.Should().Contain("result", because: "result diagnostic port must be declared~ 📊");
    }

    // ── Bool coercion ─────────────────────────────────────────────────────────────────

    /// <summary>Bool <c>true</c> input → ActivePorts contains only "true"; not "false"~ ✅</summary>
    [Fact]
    public async Task BoolTrue_ActivatesTruePort()
    {
        // Arrange
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = true });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true",
            because: "bool true must activate the true port~ ✅");
        result.ActivePorts.Should().NotContain("false");
        result.Outputs.Should().ContainKey("result").WhoseValue.Should().Be(true);
    }

    /// <summary>Bool <c>false</c> input → ActivePorts contains only "false"~ ❌</summary>
    [Fact]
    public async Task BoolFalse_ActivatesFalsePort()
    {
        // Arrange
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = false });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("false",
            because: "bool false must activate the false port~ ❌");
        result.Outputs["result"].Should().Be(false);
    }

    // ── String coercion ───────────────────────────────────────────────────────────────

    /// <summary>String "true" (case-insensitive) → true port~ 🔠</summary>
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("on")]
    public async Task StringTruthy_ActivatesTruePort(string input)
    {
        // Arrange
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = input });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true",
            because: $"'{input}' should coerce to true~ 🔠");
    }

    /// <summary>String "false" variants → false port~ 🔠</summary>
    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("off")]
    public async Task StringFalsy_ActivatesFalsePort(string input)
    {
        // Arrange
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = input });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("false",
            because: $"'{input}' should coerce to false~ 🔠");
    }

    // ── Property fallback ─────────────────────────────────────────────────────────────

    /// <summary>Input port overrides the condition property when both are provided~ 🔗</summary>
    [Fact]
    public async Task InputPort_OverridesProperty_WhenBothProvided()
    {
        // Arrange — property says false, input says true
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["condition"] = true },
            properties: new Dictionary<string, object?> { ["condition"] = "false" });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert — input wins~ 🔗
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true",
            because: "runtime input port takes priority over static property~ 🔗");
    }

    /// <summary>Property is used as fallback when no input port is connected~ 💬</summary>
    [Fact]
    public async Task Property_UsedWhenNoInputPort()
    {
        // Arrange — no input, property provides "true"
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>(),
            properties: new Dictionary<string, object?> { ["condition"] = "true" });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert — property fallback works~ 💬
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true");
    }

    // ── Both branches reachable ────────────────────────────────────────────────────────

    /// <summary>Both branches reachable across two separate runs~ 🔀</summary>
    [Fact]
    public async Task BothBranches_ReachableAcrossTwoRuns()
    {
        var resultTrue = await _module.ExecuteAsync(
            BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = true }));

        var resultFalse = await _module.ExecuteAsync(
            BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = false }));

        resultTrue.ActivePorts.Should().Contain("true");
        resultFalse.ActivePorts.Should().Contain("false");
    }

    // ── Expression evaluator integration ─────────────────────────────────────────────

    /// <summary>Complex expression uses IExpressionEvaluator from DI~ 🧮</summary>
    [Fact]
    public async Task Expression_UsesIExpressionEvaluator_WhenAvailable()
    {
        // Arrange — expression "x > 5" with evaluator returning true
        var services = MockEvaluatorServices(evaluationResult: true, expression: "x > 5");
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["condition"] = "x > 5" },
            variables: new Dictionary<string, object?> { ["x"] = 10 },
            services: services);

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true",
            because: "evaluator returned true for 'x > 5' with x=10~ 🧮");
    }

    /// <summary>Expression evaluator returning false routes to false port~ 🧮</summary>
    [Fact]
    public async Task Expression_EvaluatingFalse_ActivatesFalsePort()
    {
        // Arrange — "x > 5" with x=3, evaluator returns false
        var services = MockEvaluatorServices(evaluationResult: false, expression: "x > 5");
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["condition"] = "x > 5" },
            variables: new Dictionary<string, object?> { ["x"] = 3 },
            services: services);

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("false");
    }

    /// <summary>Unknown expression without evaluator fails with descriptive error~ 💔</summary>
    [Fact]
    public async Task InvalidExpression_NoEvaluator_Fails()
    {
        // Arrange — "x + y" can't be coerced to bool string and no evaluator is registered
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = "x + y" });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert — descriptive failure; not a crash~ 💔
        result.Success.Should().BeFalse(
            because: "an unevaluatable expression with no registered evaluator must fail gracefully~ 💔");
        result.ErrorMessage.Should().Contain("IExpressionEvaluator",
            because: "error message should mention the missing evaluator~ 🔍");
    }

    // ── Null / missing condition ───────────────────────────────────────────────────────

    /// <summary>Null condition (no input, no property) fails gracefully~ 💔</summary>
    [Fact]
    public async Task NullCondition_ReturnsFailure()
    {
        // Arrange — neither input nor property provided
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>(),
            properties: new Dictionary<string, object?>());

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeFalse(
            because: "condition is required — null input must not silently succeed~ 💔");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    // ── Result output & diagnostics ───────────────────────────────────────────────────

    /// <summary>result output always carries the evaluated bool for diagnostics~ 📊</summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task ResultOutput_ReflectsEvaluatedBool(bool input, string expectedPort)
    {
        // Arrange
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = input });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Outputs.Should().ContainKey("result",
            because: "result port carries the evaluated bool for logging/debugging~ 📊");
        result.Outputs["result"].Should().Be(input);
        result.ActivePorts.Should().Contain(expectedPort);
    }

    // ── Numeric coercion ──────────────────────────────────────────────────────────────

    /// <summary>Non-zero integer → true port (truthy semantics)~ 🔢</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(42)]
    public async Task NumericNonZero_ActivatesTruePort(int value)
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = value });
        var result = await _module.ExecuteAsync(ctx);
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("true");
    }

    /// <summary>Zero → false port~ 🔢</summary>
    [Fact]
    public async Task NumericZero_ActivatesFalsePort()
    {
        var ctx = BuildContext(inputs: new Dictionary<string, object?> { ["condition"] = 0 });
        var result = await _module.ExecuteAsync(ctx);
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("false");
    }
}

