// <copyright file="ModuleAwareWorkflowValidatorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Builtin;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// 🔌 Phase 1.4.3 (deferred) — Tests for <see cref="ModuleAwareWorkflowValidator"/>!
/// Covers module-id existence checks, property schema validation, and port name
/// validation for connections~ ✨💖
/// </summary>
public sealed class ModuleAwareWorkflowValidatorTests
{
    // CopilotNote: PassThroughModule has input "input", output "output", no properties~ 💖
    private static InMemoryModuleRegistry BuildRegistry()
    {
        var registry = new InMemoryModuleRegistry();
        registry.RegisterModule(new PassThroughModule());
        return registry;
    }

    /// <summary>
    /// Builds a minimal valid single-node workflow using PassThroughModule~ 🌸
    /// </summary>
    private static WorkflowDefinition SingleNodeWorkflow(
        string nodeId = "node1",
        string moduleId = "builtin.passthrough",
        HashMap<string, JsonElement>? properties = null)
        => new(
            Id: Guid.NewGuid(),
            Name: "Test Workflow",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(
                new NodeDefinition(nodeId, moduleId, "Test Node", properties ?? HashMap<string, JsonElement>.Empty)),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

    /// <summary>
    /// Builds a two-node workflow connected via ports~ 🔗
    /// </summary>
    private static WorkflowDefinition TwoNodeWorkflow(
        string sourcePort,
        string targetPort,
        string sourceModule = "builtin.passthrough",
        string targetModule = "builtin.passthrough")
        => new(
            Id: Guid.NewGuid(),
            Name: "Two Node Workflow",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(
                new NodeDefinition("node1", sourceModule, "Source", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("node2", targetModule, "Target", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("node1", sourcePort, "node2", targetPort)),
            Variables: HashMap<string, VariableDefinition>.Empty);

    #region Constructor Tests 🌸

    /// <summary>
    /// Constructor should throw when registry is null~ 🛑
    /// </summary>
    [Fact]
    public void Constructor_NullRegistry_ShouldThrow()
    {
        var act = () => new ModuleAwareWorkflowValidator(null!);
        act.Should().Throw<ArgumentNullException>("registry is required~ UwU");
    }

    #endregion

    #region ModuleId Existence Tests (MA001) 🔍

    /// <summary>
    /// A workflow with all nodes using registered ModuleIds should pass~ ✅
    /// </summary>
    [Fact]
    public void Validate_AllModuleIdsExist_ShouldPass()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = SingleNodeWorkflow();

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.Errors.Should().NotContain(
            e => e.Code == "MA001",
            "builtin.passthrough is registered~ 💖");
    }

    /// <summary>
    /// A node referencing an unknown ModuleId should produce MA001~ ❌
    /// </summary>
    [Fact]
    public void Validate_UnknownModuleId_ShouldProduceMA001Error()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = SingleNodeWorkflow(moduleId: "unknown.module.does.not.exist");

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.IsValid.Should().BeFalse("an unknown module ID is a hard error~ 💔");
        result.Errors.Should().Contain(
            e => e.Code == "MA001" && e.NodeId == "node1",
            "MA001 should be raised for the node with the unknown module~ UwU");
    }

    /// <summary>
    /// A node with an empty ModuleId should produce MA001~ ❌
    /// </summary>
    [Fact]
    public void Validate_EmptyModuleId_ShouldProduceMA001Error()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = SingleNodeWorkflow(moduleId: string.Empty);

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.Errors.Should().Contain(
            e => e.Code == "MA001",
            "empty ModuleId is an error~ 💔");
    }

    #endregion

    #region Property Schema Validation Tests (MA002) ⚙️

    /// <summary>🎚️ The reserved <c>outputMode</c> property is engine-handled — never MA002~ ✅.</summary>
    [Fact]
    public void Validate_ReservedOutputModeProperty_NoMA002()
    {
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var props = new HashMap<string, JsonElement>().Add("outputMode", JsonSerializer.SerializeToElement("merged"));
        var workflow = SingleNodeWorkflow(properties: props);

        var result = validator.Validate(workflow);

        result.Errors.Should().NotContain(e => e.Code == "MA002", "outputMode is a reserved engine property~ 🎚️");
    }

    /// <summary>🎚️ A merged node's <c>output</c> source port passes MA003~ ✅.</summary>
    [Fact]
    public void Validate_MergedNode_OutputSourcePort_NoMA003()
    {
        var registry = BuildRegistry();
        registry.RegisterModule(new LogModule());
        var validator = new ModuleAwareWorkflowValidator(registry);

        var props = new HashMap<string, JsonElement>()
            .Add("message", JsonSerializer.SerializeToElement("hi"))
            .Add("outputMode", JsonSerializer.SerializeToElement("merged"));
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "merged", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("log1", "builtin.log", "Log", props),
                new NodeDefinition("sink", "builtin.passthrough", "Sink", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(new ConnectionDefinition("log1", "output", "sink", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var result = validator.Validate(workflow);

        result.Errors.Should().NotContain(e => e.Code == "MA003", "merged nodes expose the reserved 'output' port~ 📦");
    }

    /// <summary>🎚️ Without merged mode, an undeclared <c>output</c> source port still fails MA003~ ❌.</summary>
    [Fact]
    public void Validate_NonMergedNode_OutputSourcePort_ProducesMA003()
    {
        var registry = BuildRegistry();
        registry.RegisterModule(new LogModule());
        var validator = new ModuleAwareWorkflowValidator(registry);

        var props = new HashMap<string, JsonElement>().Add("message", JsonSerializer.SerializeToElement("hi"));
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(), Name: "not-merged", Description: null, Version: new Version(1, 0),
            Nodes: Arr.create(
                new NodeDefinition("log1", "builtin.log", "Log", props),
                new NodeDefinition("sink", "builtin.passthrough", "Sink", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(new ConnectionDefinition("log1", "output", "sink", "input")),
            Variables: HashMap<string, VariableDefinition>.Empty);

        var result = validator.Validate(workflow);

        result.Errors.Should().Contain(
            e => e.Code == "MA003",
            "'output' is not declared on builtin.log and the node is not merged~ 💔");
    }

    /// <summary>
    /// A node with no configured properties should pass (PassThroughModule has no required properties)~ ✅
    /// </summary>
    [Fact]
    public void Validate_NodeWithNoProperties_ShouldPass()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = SingleNodeWorkflow();

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.Errors.Should().NotContain(
            e => e.Code == "MA002",
            "no properties configured means nothing to validate~ ✨");
    }

    /// <summary>
    /// A node with an unknown property key should produce MA002~ ❌
    /// </summary>
    [Fact]
    public void Validate_UnknownPropertyKey_ShouldProduceMA002Error()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);

        // PassThroughModule has no schema Properties — any property key is "unknown"~
        var badProperties = HashMap<string, JsonElement>.Empty
            .Add("nonExistentProperty", JsonDocument.Parse("\"hello\"").RootElement);

        var workflow = SingleNodeWorkflow(properties: badProperties);

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.IsValid.Should().BeFalse("an unknown property key is an error~ 💔");
        result.Errors.Should().Contain(
            e => e.Code == "MA002" && e.NodeId == "node1" && e.PropertyName == "nonExistentProperty",
            "MA002 should identify both the node and the unknown property~ UwU");
    }

    /// <summary>
    /// When a node references an unknown module, MA002 should NOT fire for that node's
    /// properties (we can't validate without a schema!)~ 🛡️
    /// </summary>
    [Fact]
    public void Validate_UnknownModuleWithProperties_ShouldOnlyProduceMA001NotMA002()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);

        var properties = HashMap<string, JsonElement>.Empty
            .Add("someProperty", JsonDocument.Parse("\"value\"").RootElement);

        var workflow = SingleNodeWorkflow(moduleId: "unknown.module", properties: properties);

        // Act
        var result = validator.Validate(workflow);

        // Assert — MA001 yes, MA002 no (can't check schema without the module)~
        result.Errors.Should().Contain(e => e.Code == "MA001", "unknown module triggers MA001~ 🎯");
        result.Errors.Should().NotContain(e => e.Code == "MA002",
            "MA002 should not fire when the module is unknown (no schema to check against)~ 💖");
    }

    #endregion

    #region Port Name Validation Tests (MA003 / MA004) 🔗

    /// <summary>
    /// A connection using valid port names should pass~ ✅
    /// </summary>
    [Fact]
    public void Validate_ValidPortNames_ShouldPass()
    {
        // Arrange — PassThrough: output "output" → input "input"~
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = TwoNodeWorkflow(sourcePort: "output", targetPort: "input");

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.Errors.Should().NotContain(
            e => e.Code == "MA003" || e.Code == "MA004",
            "valid port names should produce no port errors~ ✨");
    }

    /// <summary>
    /// A connection using an invalid source port name should produce MA003~ ❌
    /// </summary>
    [Fact]
    public void Validate_InvalidSourcePort_ShouldProduceMA003Error()
    {
        // Arrange — PassThrough only has "output", not "nonExistentOutput"~
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = TwoNodeWorkflow(sourcePort: "nonExistentOutput", targetPort: "input");

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.IsValid.Should().BeFalse("bad source port is an error~ 💔");
        result.Errors.Should().Contain(
            e => e.Code == "MA003" && e.NodeId == "node1",
            "MA003 should reference the source node~ UwU");
    }

    /// <summary>
    /// A connection using an invalid target port name should produce MA004~ ❌
    /// </summary>
    [Fact]
    public void Validate_InvalidTargetPort_ShouldProduceMA004Error()
    {
        // Arrange — PassThrough only has "input", not "badInput"~
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = TwoNodeWorkflow(sourcePort: "output", targetPort: "badInput");

        // Act
        var result = validator.Validate(workflow);

        // Assert
        result.IsValid.Should().BeFalse("bad target port is an error~ 💔");
        result.Errors.Should().Contain(
            e => e.Code == "MA004" && e.NodeId == "node2",
            "MA004 should reference the target node~ UwU");
    }

    /// <summary>
    /// Base structural errors (e.g. cycle, missing start node) should still appear
    /// alongside module-aware errors~ 🏗️
    /// </summary>
    [Fact]
    public void Validate_BaseErrorsAndModuleErrors_ShouldBothAppear()
    {
        // Arrange — cycle AND unknown module in same workflow~
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);

        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Cycle Test",
            Description: null,
            Version: new Version(1, 0, 0),
            Nodes: Arr.create(
                new NodeDefinition("n1", "unknown.module", "N1", HashMap<string, JsonElement>.Empty),
                new NodeDefinition("n2", "builtin.passthrough", "N2", HashMap<string, JsonElement>.Empty)),
            Connections: Arr.create(
                new ConnectionDefinition("n1", "output", "n2", "input"),
                new ConnectionDefinition("n2", "output", "n1", "input")), // ← cycle!
            Variables: HashMap<string, VariableDefinition>.Empty);

        // Act
        var result = validator.Validate(workflow);

        // Assert — both base (WF012 cycle) and module-aware (MA001 unknown module) errors~
        result.Errors.Should().Contain(
            e => e.Code == "WF012",
            "base validator should detect the cycle~ 🔄");
        result.Errors.Should().Contain(
            e => e.Code == "MA001",
            "module validator should detect the unknown module~ 🎯");
    }

    /// <summary>
    /// Port validation should be skipped for connections whose nodes failed module
    /// resolution (MA001) — no schema means no port check~ 🛡️
    /// </summary>
    [Fact]
    public void Validate_ConnectionWithUnknownModuleNodes_ShouldNotProducePortErrors()
    {
        // Arrange — both nodes use unknown modules; ports are also invalid but we can't check~
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);
        var workflow = TwoNodeWorkflow(
            sourcePort: "nonExistent",
            targetPort: "alsoNonExistent",
            sourceModule: "unknown.a",
            targetModule: "unknown.b");

        // Act
        var result = validator.Validate(workflow);

        // Assert — MA001 fires but MA003/MA004 should NOT (no schema to compare against)~
        result.Errors.Should().Contain(e => e.Code == "MA001");
        result.Errors.Should().NotContain(
            e => e.Code == "MA003" || e.Code == "MA004",
            "cannot validate ports when the module schema is unavailable~ 💖");
    }

    /// <summary>
    /// Validate should throw ArgumentNullException for a null workflow~ 🛑
    /// </summary>
    [Fact]
    public void Validate_NullWorkflow_ShouldThrow()
    {
        // Arrange
        var registry = BuildRegistry();
        var validator = new ModuleAwareWorkflowValidator(registry);

        // Act & Assert
        var act = () => validator.Validate(null!);
        act.Should().Throw<ArgumentNullException>("null workflow is not allowed~ UwU");
    }

    #endregion
}




