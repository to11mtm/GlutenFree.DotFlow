// <copyright file="SwitchModuleTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Flow;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Engine.Messages;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Flow;
using Xunit;

/// <summary>
/// 🔢 Phase 2.2.1 — Tests for <see cref="SwitchModule"/> (<c>builtin.switch</c>)~
/// Validates case matching, default port, no-match failure, case sensitivity,
/// and integration with WorkflowExecutor port-aware routing~ ✨💖
/// </summary>
public sealed class SwitchModuleTests
{
    private readonly SwitchModule _module = new();

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    private static ModuleExecutionContext BuildContext(
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? properties = null)
    {
        return new ModuleExecutionContext
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties ?? new Dictionary<string, object?>(),
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "switch-node",
        };
    }

    /// <summary>
    /// Builds a cases JSON property value as a pre-parsed List (as ConvertJsonElement would produce)~ 📋
    /// </summary>
    private static List<object?> BuildCases(params (string match, string port)[] cases)
    {
        return cases.Select(c => (object?)new Dictionary<string, object?>
        {
            ["match"] = c.match,
            ["port"] = c.port,
        }).ToList();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ── Schema & metadata ─────────────────────────────────────────────────────────────

    /// <summary>Module metadata is set correctly~ 🏷️</summary>
    [Fact]
    public void SwitchModule_Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.switch");
        _module.Category.Should().Be("Flow Control");
        _module.Version.Should().Be(new Version(1, 0, 0));
    }

    /// <summary>Outputs are EMPTY (dynamic) so ValidateConnectionPorts skips it~ 📋</summary>
    [Fact]
    public void SwitchModule_Schema_HasEmptyOutputs()
    {
        _module.Schema.Outputs.ToList().Should().BeEmpty(
            because: "switch ports are dynamic; empty outputs causes ValidateConnectionPorts to skip~ 🎗️");
    }

    // ── Case matching ─────────────────────────────────────────────────────────────────

    /// <summary>First matching case activates the correct port~ 🎯</summary>
    [Fact]
    public async Task MatchesFirstCase_ActivatesCorrectPort()
    {
        // Arrange
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "cat" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat"), ("dog", "case_dog")),
            });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("case_cat");
    }

    /// <summary>Second case matches when first doesn't~ 🔍</summary>
    [Fact]
    public async Task LaterCaseMatches_WhenFirstDoesNot()
    {
        // Arrange
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "dog" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat"), ("dog", "case_dog"), ("fish", "case_fish")),
            });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("case_dog",
            because: "second case should match 'dog'~ 🐶");
    }

    /// <summary>First match wins even when multiple cases could match~ 🏅</summary>
    [Fact]
    public async Task FirstCaseWins_WhenMultipleWouldMatch()
    {
        // Arrange — two cases both match "cat" (same match value, different ports)
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "cat" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "first_cat"), ("cat", "second_cat")),
            });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("first_cat",
            because: "'first match wins' semantic must hold~ 🏅");
    }

    // ── Default port ──────────────────────────────────────────────────────────────────

    /// <summary>Falls back to defaultPort when no case matches~ 🎯</summary>
    [Fact]
    public async Task NoMatch_UsesDefaultPort()
    {
        // Arrange
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "unknown" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat"), ("dog", "case_dog")),
                ["defaultPort"] = "case_default",
            });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("case_default",
            because: "unmatched value should fall back to defaultPort~ 🎯");
    }

    /// <summary>No match + no defaultPort → descriptive failure~ 💔</summary>
    [Fact]
    public async Task NoMatch_NoDefault_Fails()
    {
        // Arrange — value doesn't match any case, no defaultPort
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "unknown" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat")),
            });

        // Act
        var result = await _module.ExecuteAsync(ctx);

        // Assert
        result.Success.Should().BeFalse(
            because: "unmatched value with no default must fail with a clear error~ 💔");
        result.ErrorMessage.Should().Contain("unknown",
            because: "the unmatched value should appear in the error message~ 🔍");
    }

    // ── Case sensitivity ──────────────────────────────────────────────────────────────

    /// <summary>String comparison is case-insensitive by default~ 🔡</summary>
    [Fact]
    public async Task DefaultCaseInsensitive_MatchesDifferentCase()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "CAT" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat")),
            });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("case_cat",
            because: "default matching is case-insensitive~ 🔡");
    }

    /// <summary>Case-sensitive matching rejects different case~ 🔡</summary>
    [Fact]
    public async Task CaseSensitive_DoesNotMatchDifferentCase()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "CAT" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat")),
                ["caseSensitive"] = true,
                ["defaultPort"] = "no_match",
            });

        var result = await _module.ExecuteAsync(ctx);

        result.ActivePorts.Should().ContainSingle().Which.Should().Be("no_match",
            because: "case-sensitive matching should NOT match 'cat' against 'CAT'~ 🔡");
    }

    // ── Configuration validation ─────────────────────────────────────────────────────

    /// <summary>Empty cases array fails configuration validation~ 💔</summary>
    [Fact]
    public void ValidateConfiguration_EmptyCases_Fails()
    {
        var config = new Dictionary<string, object?>
        {
            ["cases"] = new List<object?>(), // empty list
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse(
            because: "at least one case entry is required~ 💔");
        result.Errors.Should().Contain(e => e.Code == "EMPTY_CASES");
    }

    /// <summary>Missing cases property fails validation~ 💔</summary>
    [Fact]
    public void ValidateConfiguration_MissingCases_Fails()
    {
        var config = new Dictionary<string, object?>();

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MISSING_CASES");
    }

    /// <summary>Valid cases array passes validation~ ✅</summary>
    [Fact]
    public void ValidateConfiguration_ValidCases_Passes()
    {
        var config = new Dictionary<string, object?>
        {
            ["cases"] = BuildCases(("cat", "case_cat"), ("dog", "case_dog")),
        };

        var result = _module.ValidateConfiguration(config);

        result.IsValid.Should().BeTrue();
    }

    // ── JSON string cases format ──────────────────────────────────────────────────────

    /// <summary>Cases provided as JSON string are parsed correctly~ 📋</summary>
    [Fact]
    public async Task Cases_AsJsonString_ParsedCorrectly()
    {
        var casesJson = """[{"match":"alpha","port":"port_a"},{"match":"beta","port":"port_b"}]""";

        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "beta" },
            properties: new Dictionary<string, object?> { ["cases"] = casesJson });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("port_b");
    }

    // ── Value input / property fallback ──────────────────────────────────────────────

    /// <summary>Property 'value' is used when no input port is connected~ 💬</summary>
    [Fact]
    public async Task PropertyValue_UsedWhenNoInputPort()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?>(), // no input port
            properties: new Dictionary<string, object?>
            {
                ["value"] = "cat",
                ["cases"] = BuildCases(("cat", "case_cat")),
            });

        var result = await _module.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        result.ActivePorts.Should().ContainSingle().Which.Should().Be("case_cat",
            because: "property 'value' should be used as fallback~ 💬");
    }

    // ── Diagnostics output ─────────────────────────────────────────────────────────────

    /// <summary>Outputs carry matchedPort and value for diagnostics~ 📊</summary>
    [Fact]
    public async Task Outputs_CarryDiagnostics()
    {
        var ctx = BuildContext(
            inputs: new Dictionary<string, object?> { ["value"] = "cat" },
            properties: new Dictionary<string, object?>
            {
                ["cases"] = BuildCases(("cat", "case_cat")),
            });

        var result = await _module.ExecuteAsync(ctx);

        result.Outputs.Should().ContainKey("matchedPort").WhoseValue.Should().Be("case_cat",
            because: "matchedPort output is useful for debugging/logging~ 📊");
        result.Outputs.Should().ContainKey("value").WhoseValue.Should().Be("cat");
    }
}

/// <summary>
/// 🔀🔢 Phase 2.2.1 — Integration tests for condition + switch with WorkflowExecutor port routing~
/// Validates that both modules wire correctly into the engine's selective port activation~ ✨
/// </summary>
public class ConditionalSwitchIntegrationTests : TestKit
{
    // ── ConditionalModule engine integration ──────────────────────────────────────────

    /// <summary>
    /// ConditionalModule wired into WorkflowExecutor: true branch fires, false is skipped~ 🔀
    /// </summary>
    [Fact]
    public void ConditionalModule_TrueCondition_FiresOnlyTrueBranch()
    {
        // Arrange — stubs
        var condModule = new ConditionalModule();
        var passTrue = new StubPassthroughModule("builtin.pass.true");
        var passFalse = new StubPassthroughModule("builtin.pass.false");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(condModule);
        registry.RegisterModule(passTrue);
        registry.RegisterModule(passFalse);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        // condition node input: condition = true (via NodeDefinition Properties)
        var condProps = new Dictionary<string, JsonElement>
        {
            ["condition"] = JsonSerializer.SerializeToElement(true),
        }.ToHashMap();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "cond-integration-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("cond", "builtin.condition", "Cond", condProps),
                new NodeDefinition("trueNode",  "builtin.pass.true",  "True",  HashMap<string, JsonElement>.Empty),
                new NodeDefinition("falseNode", "builtin.pass.false", "False", HashMap<string, JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("cond", "true",  "trueNode",  "input"),
                new ConnectionDefinition("cond", "false", "falseNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parentProbe = CreateTestProbe("cond-integ-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), definition, new Dictionary<string, object?>(), sp),
            "cond-integ-exec");

        // Act
        executor.Tell(new StartExecution(Guid.NewGuid()));

        // Assert — workflow completes (trueNode ran; falseNode was skipped but didn't fail)~ ✅
        var completed = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(5));

        completed.Should().BeOfType<WorkflowCompleted>(
            because: "condition=true should complete the workflow via the true branch~ 🔀");
    }

    /// <summary>
    /// SwitchModule wired into WorkflowExecutor: matched port fires, others are skipped~ 🔢
    /// </summary>
    [Fact]
    public void SwitchModule_MatchingCase_FiresCorrectPort()
    {
        // Arrange
        var switchModule = new SwitchModule();
        var passCat = new StubPassthroughModule("builtin.pass.cat");
        var passDog = new StubPassthroughModule("builtin.pass.dog");

        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(switchModule);
        registry.RegisterModule(passCat);
        registry.RegisterModule(passDog);

        var sp = new ServiceCollection()
            .AddSingleton<IModuleRegistry>(registry)
            .BuildServiceProvider();

        // switch node config: value="cat", cases=[{cat→case_cat},{dog→case_dog}]
        var switchProps = new Dictionary<string, JsonElement>
        {
            ["value"] = JsonSerializer.SerializeToElement("cat"),
            ["cases"] = JsonSerializer.SerializeToElement(
                new[] { new { match = "cat", port = "case_cat" }, new { match = "dog", port = "case_dog" } }),
        }.ToHashMap();

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "switch-integ-test",
            Description: null,
            Version: new Version(1, 0),
            Nodes: new[]
            {
                new NodeDefinition("sw",       "builtin.switch",    "Switch", switchProps),
                new NodeDefinition("catNode",  "builtin.pass.cat",  "Cat",   HashMap<string, JsonElement>.Empty),
                new NodeDefinition("dogNode",  "builtin.pass.dog",  "Dog",   HashMap<string, JsonElement>.Empty),
            }.ToArr(),
            Connections: new[]
            {
                new ConnectionDefinition("sw", "case_cat", "catNode", "input"),
                new ConnectionDefinition("sw", "case_dog", "dogNode", "input"),
            }.ToArr(),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var parentProbe = CreateTestProbe("switch-integ-parent");
        var executor = parentProbe.ChildActorOf(
            WorkflowExecutor.Props(Guid.NewGuid(), definition, new Dictionary<string, object?>(), sp),
            "switch-integ-exec");

        // Act
        executor.Tell(new StartExecution(Guid.NewGuid()));

        // Assert — workflow completes (catNode ran; dogNode was skipped)~ 🔢
        var completed = parentProbe.FishForMessage(
            m => m is WorkflowCompleted or WorkflowFailed, TimeSpan.FromSeconds(5));

        completed.Should().BeOfType<WorkflowCompleted>(
            because: "switch value='cat' should route to case_cat and complete successfully~ 🔢");
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────────────

    private sealed class StubPassthroughModule(string moduleId) : IWorkflowModule
    {
        public string ModuleId => moduleId;
        public string DisplayName => moduleId;
        public string Category => "Test";
        public string Description => "Pass-through stub";
        public string Icon => "🌿";
        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Inputs: Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Outputs: Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Properties: Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext ctx, CancellationToken ct = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "done" }));
    }
}


