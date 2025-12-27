// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using FluentAssertions;
using LanguageExt;
using static LanguageExt.Prelude;
using System.Text.Json;
using Workflow.Core.Models;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Tests for ModuleSchema, PortDefinition, ModulePropertyDefinition, and related types! 📋✨
/// </summary>
public class PropertySystemTests
{
    #region PropertyEditorType Tests

    [Fact]
    public void PropertyEditorType_HasAllExpectedValues()
    {
        // Assert - Verify enum has all editor types
        var types = Enum.GetValues<PropertyEditorType>();
        types.Should().Contain(new[]
        {
            PropertyEditorType.Text,
            PropertyEditorType.MultilineText,
            PropertyEditorType.Number,
            PropertyEditorType.Boolean,
            PropertyEditorType.Dropdown,
            PropertyEditorType.FilePath,
            PropertyEditorType.DirectoryPath,
            PropertyEditorType.ConnectionString,
            PropertyEditorType.Expression,
            PropertyEditorType.Json,
            PropertyEditorType.Code,
        });
        types.Should().HaveCount(11);
    }

    #endregion

    #region ValidationRuleType Tests

    [Fact]
    public void ValidationRuleType_HasAllExpectedValues()
    {
        // Assert - Verify enum has all validation rule types
        var types = Enum.GetValues<ValidationRuleType>();
        types.Should().Contain(new[]
        {
            ValidationRuleType.MinLength,
            ValidationRuleType.MaxLength,
            ValidationRuleType.Min,
            ValidationRuleType.Max,
            ValidationRuleType.Regex,
            ValidationRuleType.Enum,
            ValidationRuleType.Custom,
        });
        types.Should().HaveCount(7);
    }

    #endregion

    #region PortDefinition Tests

    [Fact]
    public void PortDefinition_Constructor_SetsAllProperties()
    {
        // Act
        var portDef = new PortDefinition(
            Name: "input",
            DisplayName: "Input Port",
            DataType: typeof(string),
            Description: "An input port",
            IsRequired: true,
            DefaultValue: "default");

        // Assert
        portDef.Name.Should().Be("input");
        portDef.DisplayName.Should().Be("Input Port");
        portDef.DataType.Should().Be(typeof(string));
        portDef.Description.Should().Be("An input port");
        portDef.IsRequired.Should().BeTrue();
        portDef.DefaultValue.Should().Be("default");
    }

    [Fact]
    public void PortDefinition_OptionalParameters_HaveDefaults()
    {
        // Arrange & Act
        var portDef = new PortDefinition(
            Name: "output",
            DisplayName: "Output",
            DataType: typeof(int));

        // Assert
        portDef.Description.Should().BeNull();
        portDef.IsRequired.Should().BeTrue(); // Default for ports is true
        portDef.DefaultValue.Should().BeNull();
    }

    [Fact]
    public void PortDefinition_Create_WithNameAndType()
    {
        // Act
        var portDef = PortDefinition.Create("data", typeof(object), isRequired: false);

        // Assert
        portDef.Name.Should().Be("data");
        portDef.DisplayName.Should().Be("data");
        portDef.DataType.Should().Be(typeof(object));
        portDef.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void PortDefinition_CreateGeneric_SetsTypeFromGenericParameter()
    {
        // Act
        var portDef = PortDefinition.Create<string>("message");

        // Assert
        portDef.Name.Should().Be("message");
        portDef.DataType.Should().Be(typeof(string));
        portDef.IsRequired.Should().BeTrue();
    }

    #endregion

    #region ModulePropertyDefinition Tests

    [Fact]
    public void ModulePropertyDefinition_Constructor_SetsAllProperties()
    {
        // Arrange
        var allowedValues = Arr.create<object>("option1", "option2", "option3");
        var rules = Arr.create(new ValidationRule(ValidationRuleType.MinLength));
        var metadata = HashMap(("ui:group", "Advanced"));

        // Act
        var propDef = new ModulePropertyDefinition(
            Name: "apiKey",
            DisplayName: "API Key",
            DataType: typeof(string),
            Description: "The API key for authentication",
            IsRequired: true,
            DefaultValue: null,
            EditorType: PropertyEditorType.Text,
            AllowedValues: allowedValues,
            ValidationRules: rules,
            DisplayMetadata: metadata);

        // Assert
        propDef.Name.Should().Be("apiKey");
        propDef.DisplayName.Should().Be("API Key");
        propDef.DataType.Should().Be(typeof(string));
        propDef.Description.Should().Be("The API key for authentication");
        propDef.IsRequired.Should().BeTrue();
        propDef.EditorType.Should().Be(PropertyEditorType.Text);
        propDef.AllowedValues!.Value.Count().Should().Be(3);
        propDef.ValidationRules!.Value.Count().Should().Be(1);
        propDef.DisplayMetadata!.Value.ContainsKey("ui:group").Should().BeTrue();
    }

    [Fact]
    public void ModulePropertyDefinition_OptionalParameters_HaveDefaults()
    {
        // Arrange & Act
        var propDef = new ModulePropertyDefinition(
            Name: "count",
            DisplayName: "Count",
            DataType: typeof(int));

        // Assert
        propDef.Description.Should().BeNull();
        propDef.IsRequired.Should().BeFalse(); // Default for properties is false
        propDef.DefaultValue.Should().BeNull();
        propDef.EditorType.Should().Be(PropertyEditorType.Text);
        propDef.AllowedValues.Should().BeNull();
        propDef.ValidationRules.Should().BeNull();
        propDef.DisplayMetadata.Should().BeNull();
    }

    [Fact]
    public void ModulePropertyDefinition_Create_WithNameAndType()
    {
        // Act
        var propDef = ModulePropertyDefinition.Create("timeout", typeof(int), isRequired: true);

        // Assert
        propDef.Name.Should().Be("timeout");
        propDef.DisplayName.Should().Be("timeout");
        propDef.DataType.Should().Be(typeof(int));
        propDef.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void ModulePropertyDefinition_CreateGeneric_SetsTypeFromGenericParameter()
    {
        // Act
        var propDef = ModulePropertyDefinition.Create<bool>("enabled");

        // Assert
        propDef.Name.Should().Be("enabled");
        propDef.DataType.Should().Be(typeof(bool));
        propDef.IsRequired.Should().BeFalse();
    }

    #endregion

    #region ValidationRule Tests

    [Fact]
    public void ValidationRule_Constructor_SetsAllProperties()
    {
        // Arrange
        var parameters = HashMap(
            ("min", (object)5),
            ("max", (object)100));

        // Act
        var rule = new ValidationRule(
            ValidationRuleType.MinLength,
            parameters,
            "Value must be between 5 and 100 characters");

        // Assert
        rule.RuleType.Should().Be(ValidationRuleType.MinLength);
        rule.Parameters.Should().BeEquivalentTo(parameters);
        rule.ErrorMessage.Should().Be("Value must be between 5 and 100 characters");
    }

    [Fact]
    public void ValidationRule_OptionalParameters_DefaultToNull()
    {
        // Arrange & Act
        var rule = new ValidationRule(ValidationRuleType.Regex);

        // Assert
        rule.Parameters.Should().BeNull();
        rule.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region ModuleSchema Tests

    [Fact]
    public void ModuleSchema_Constructor_SetsAllCollections()
    {
        // Arrange
        var inputs = Arr.create(
            new PortDefinition("input1", "Input 1", typeof(string)));
        var outputs = Arr.create(
            new PortDefinition("output1", "Output 1", typeof(int)));
        var properties = Arr.create(
            new ModulePropertyDefinition("apiKey", "API Key", typeof(string), IsRequired: true));

        // Act
        var schema = new ModuleSchema(inputs, outputs, properties);

        // Assert
        schema.Inputs.Count().Should().Be(1);
        schema.Inputs[0].Name.Should().Be("input1");
        schema.Outputs.Count().Should().Be(1);
        schema.Outputs[0].Name.Should().Be("output1");
        schema.Properties.Count().Should().Be(1);
        schema.Properties[0].Name.Should().Be("apiKey");
    }

    [Fact]
    public void ModuleSchema_Empty_HasNoElements()
    {
        // Act
        var schema = ModuleSchema.Empty;

        // Assert
        schema.Inputs.Count().Should().Be(0);
        schema.Outputs.Count().Should().Be(0);
        schema.Properties.Count().Should().Be(0);
    }

    [Fact]
    public void ModuleSchema_WithLanguageExtArr_SupportsStructuralEquality()
    {
        // Arrange
        var inputs1 = Arr.create(PortDefinition.Create<string>("input"));
        var inputs2 = Arr.create(PortDefinition.Create<string>("input"));

        var schema1 = new ModuleSchema(inputs1, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);
        var schema2 = new ModuleSchema(inputs2, Arr<PortDefinition>.Empty, Arr<ModulePropertyDefinition>.Empty);

        // Assert - Arr provides structural equality
        schema1.Should().Be(schema2);
    }

    #endregion

    #region VariableDefinition Tests

    [Fact]
    public void VariableDefinition_Constructor_SetsAllProperties()
    {
        // Arrange
        var initialValue = JsonDocument.Parse("42").RootElement;

        // Act
        var varDef = new VariableDefinition(
            "counter",
            PropertyType.Int,
            initialValue,
            "A counter variable");

        // Assert
        varDef.Name.Should().Be("counter");
        varDef.Type.Should().Be(PropertyType.Int);
        varDef.InitialValue.Should().NotBeNull();
        varDef.Description.Should().Be("A counter variable");
    }

    [Fact]
    public void VariableDefinition_OptionalParameters_DefaultToNull()
    {
        // Arrange & Act
        var varDef = new VariableDefinition("flag", PropertyType.Boolean);

        // Assert
        varDef.InitialValue.Should().BeNull();
        varDef.Description.Should().BeNull();
    }

    #endregion

    #region PropertyType Tests (for VariableDefinition compatibility)

    [Fact]
    public void PropertyType_HasAllExpectedValues()
    {
        // Assert - Verify enum has all property types
        var types = Enum.GetValues<PropertyType>();
        types.Should().Contain(new[]
        {
            PropertyType.String,
            PropertyType.Int,
            PropertyType.Long,
            PropertyType.Decimal,
            PropertyType.Boolean,
            PropertyType.DateTime,
            PropertyType.TimeSpan,
            PropertyType.Guid,
            PropertyType.Object,
            PropertyType.Array,
            PropertyType.Connection,
            PropertyType.Variable,
        });
        types.Should().HaveCount(12);
    }

    #endregion
}

