// <copyright file="ModuleValidatorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// ✅ Phase 1.4.3 — Tests for ModuleValidator!
/// Validates module ID format, metadata completeness, schema correctness,
/// strict mode enforcement, and registry integration. UwU ✨💖
/// </summary>
public class ModuleValidatorTests
{
    private readonly ModuleValidator _validator = new();

    #region Valid Module Tests ✅

    /// <summary>
    /// A well-formed module should pass validation with flying colors~ ✅✨
    /// </summary>
    [Fact]
    public void Validate_ValidModule_ShouldPass()
    {
        // Arrange
        var module = new PassThroughModule();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeTrue("PassThroughModule is a well-formed module~ uwu");
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Module ID Validation Tests 🏷️

    /// <summary>
    /// Empty ModuleId should fail validation~ ❌
    /// </summary>
    [Fact]
    public void Validate_EmptyModuleId_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithModuleId("").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD001");
    }

    /// <summary>
    /// Module ID with uppercase start should fail (naming convention)~ ❌
    /// </summary>
    [Fact]
    public void Validate_UppercaseModuleId_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithModuleId("BadModule").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD003");
    }

    /// <summary>
    /// Module ID with special characters should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_SpecialCharsModuleId_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithModuleId("bad module!@#").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD003");
    }

    #endregion

    #region Metadata Validation Tests 📝

    /// <summary>
    /// Missing DisplayName should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_MissingDisplayName_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithDisplayName("").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD010");
    }

    /// <summary>
    /// Missing Description should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_MissingDescription_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithDescription("").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD011");
    }

    /// <summary>
    /// Missing Category should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_MissingCategory_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithCategory("").Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD012");
    }

    /// <summary>
    /// Null Version should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_NullVersion_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithVersion(null!).Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD013");
    }

    #endregion

    #region Schema Validation Tests 📋

    /// <summary>
    /// Duplicate input port names should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_DuplicateInputPortNames_ShouldFail()
    {
        // Arrange
        var schema = new ModuleSchema(
            Inputs: Arr.create(
                PortDefinition.Create<string>("input1"),
                PortDefinition.Create<string>("input1")),
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);
        var module = new BadModuleBuilder().WithSchema(schema).Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD022" && e.Message.Contains("input1"));
    }

    /// <summary>
    /// Duplicate output port names should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_DuplicateOutputPortNames_ShouldFail()
    {
        // Arrange
        var schema = new ModuleSchema(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr.create(
                PortDefinition.Create<string>("output1"),
                PortDefinition.Create<string>("output1")),
            Properties: Arr<ModulePropertyDefinition>.Empty);
        var module = new BadModuleBuilder().WithSchema(schema).Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD022" && e.Message.Contains("output1"));
    }

    /// <summary>
    /// Duplicate property names should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_DuplicatePropertyNames_ShouldFail()
    {
        // Arrange
        var schema = new ModuleSchema(
            Inputs: Arr<PortDefinition>.Empty,
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr.create(
                ModulePropertyDefinition.Create<string>("prop1"),
                ModulePropertyDefinition.Create<string>("prop1")));
        var module = new BadModuleBuilder().WithSchema(schema).Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD031" && e.Message.Contains("prop1"));
    }

    /// <summary>
    /// Port with null DataType should fail~ ❌
    /// </summary>
    [Fact]
    public void Validate_PortWithNullDataType_ShouldFail()
    {
        // Arrange
        var schema = new ModuleSchema(
            Inputs: Arr.create(
                new PortDefinition("input1", "Input 1", null!)),
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);
        var module = new BadModuleBuilder().WithSchema(schema).Build();

        // Act
        var result = _validator.Validate(module);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD021");
    }

    #endregion

    #region Strict Mode Tests 🔒

    /// <summary>
    /// Strict mode should catch missing descriptions on ports~ 🔒
    /// </summary>
    [Fact]
    public void Validate_StrictMode_MissingPortDescription_ShouldFail()
    {
        // Arrange — port with no description
        var schema = new ModuleSchema(
            Inputs: Arr.create(
                new PortDefinition("input1", "Input 1", typeof(string), Description: null)),
            Outputs: Arr<PortDefinition>.Empty,
            Properties: Arr<ModulePropertyDefinition>.Empty);
        var module = new BadModuleBuilder().WithSchema(schema).Build();

        // Act
        var result = _validator.Validate(module, strict: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD023");
    }

    /// <summary>
    /// Strict mode should catch missing Icon~ 🔒
    /// </summary>
    [Fact]
    public void Validate_StrictMode_MissingIcon_ShouldFail()
    {
        // Arrange
        var module = new BadModuleBuilder().WithIcon("").Build();

        // Act
        var result = _validator.Validate(module, strict: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "MOD014");
    }

    /// <summary>
    /// Non-strict mode should NOT flag missing Icon~ 🔓
    /// </summary>
    [Fact]
    public void Validate_NonStrictMode_MissingIcon_ShouldPass()
    {
        // Arrange — empty icon in non-strict mode
        var module = new BadModuleBuilder().WithIcon("").Build();

        // Act
        var result = _validator.Validate(module, strict: false);

        // Assert — the icon check only applies in strict mode
        result.Errors.Should().NotContain(e => e.Code == "MOD014");
    }

    #endregion

    #region Registry Integration Tests 🔗

    /// <summary>
    /// Wiring ModuleValidator into registry: invalid module should be rejected.
    /// This tests the intent — actual wiring is Phase 1.4.3 wire step~ 🔌
    /// </summary>
    [Fact]
    public void Validate_InvalidModule_ShouldReturnErrors()
    {
        // Arrange — a module with everything wrong
        var module = new BadModuleBuilder()
            .WithModuleId("")
            .WithDisplayName("")
            .WithDescription("")
            .WithCategory("")
            .WithVersion(null!)
            .Build();

        // Act
        var result = _validator.Validate(module);

        // Assert — should have multiple errors accumulated
        result.IsValid.Should().BeFalse();
        result.Errors.Count.Should().BeGreaterThanOrEqualTo(4, "multiple things are wrong~ uwu");
    }

    #endregion

    #region Test Helper: BadModuleBuilder 🧪

    /// <summary>
    /// 🧪 A builder for creating test modules with configurable invalid properties~ 🔧
    /// CopilotNote: Using builder pattern so each test can set just the one bad field
    /// while keeping everything else valid. Very clean, very kawaii! ✨
    /// </summary>
    private sealed class BadModuleBuilder
    {
        private string _moduleId = "test.valid-module";
        private string _displayName = "Valid Test Module";
        private string _category = "Testing";
        private string _description = "A valid test module for validation testing.";
        private string _icon = "🧪";
        private Version _version = new(1, 0, 0);
        private ModuleSchema _schema = ModuleSchema.Empty;

        public BadModuleBuilder WithModuleId(string id) { _moduleId = id; return this; }

        public BadModuleBuilder WithDisplayName(string name) { _displayName = name; return this; }

        public BadModuleBuilder WithCategory(string cat) { _category = cat; return this; }

        public BadModuleBuilder WithDescription(string desc) { _description = desc; return this; }

        public BadModuleBuilder WithIcon(string icon) { _icon = icon; return this; }

        public BadModuleBuilder WithVersion(Version ver) { _version = ver; return this; }

        public BadModuleBuilder WithSchema(ModuleSchema schema) { _schema = schema; return this; }

        public IWorkflowModule Build() => new ConfigurableTestModule(
            _moduleId, _displayName, _category, _description, _icon, _version, _schema);
    }

    /// <summary>
    /// 🧪 A fully configurable test module for validation testing~ 🔧
    /// </summary>
    private sealed class ConfigurableTestModule : IWorkflowModule
    {
        public ConfigurableTestModule(
            string moduleId,
            string displayName,
            string category,
            string description,
            string icon,
            Version version,
            ModuleSchema schema)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            Category = category;
            Description = description;
            Icon = icon;
            Version = version;
            Schema = schema;
        }

        public string ModuleId { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Description { get; }

        public string Icon { get; }

        public Version Version { get; }

        public ModuleSchema Schema { get; }

        public Task<ModuleResult> ExecuteAsync(
            ModuleExecutionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    #endregion
}

