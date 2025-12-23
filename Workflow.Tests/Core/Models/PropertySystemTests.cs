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
/// Tests for ModuleSchema, PropertyDefinition, and related types! 📋
/// </summary>
public class PropertySystemTests
{
	[Fact]
	public void PropertyType_HasAllExpectedValues()
	{
		// Assert - Verify enum has all 12 property types
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
			PropertyType.Variable
		});
		types.Should().HaveCount(12);
	}

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
			ValidationRuleType.Custom
		});
		types.Should().HaveCount(7);
	}

	[Fact]
	public void PropertyDefinition_Constructor_SetsAllProperties()
	{
		// Arrange
		var defaultValue = JsonDocument.Parse("\"test\"").RootElement;
		var rules = Arr.create(new ValidationRule(ValidationRuleType.MinLength));
		var metadata = HashMap(("ui:widget", "textarea"));

		// Act
		var propDef = new PropertyDefinition(
			"username",
			PropertyType.String,
			"The user's name",
			IsRequired: true,
			DefaultValue: defaultValue,
			ValidationRules: rules,
			DisplayMetadata: metadata);

		// Assert
		propDef.Name.Should().Be("username");
		propDef.Type.Should().Be(PropertyType.String);
		propDef.Description.Should().Be("The user's name");
		propDef.IsRequired.Should().BeTrue();
		propDef.DefaultValue.Should().NotBeNull();
		propDef.ValidationRules!.Value.Count().Should().Be(1);
		propDef.DisplayMetadata!.Value.ContainsKey("ui:widget").Should().BeTrue();
	}

	[Fact]
	public void PropertyDefinition_OptionalParameters_HaveDefaults()
	{
		// Arrange & Act
		var propDef = new PropertyDefinition(
			"count",
			PropertyType.Int,
			"A counter");

		// Assert
		propDef.IsRequired.Should().BeFalse();
		propDef.DefaultValue.Should().BeNull();
		propDef.ValidationRules.Should().BeNull();
		propDef.DisplayMetadata.Should().BeNull();
	}

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

	[Fact]
	public void ModuleSchema_Constructor_SetsAllCollections()
	{
		// Arrange
		var inputs = Arr.create(
			new PropertyDefinition("input1", PropertyType.String, "Input 1"));
		var outputs = Arr.create(
			new PropertyDefinition("output1", PropertyType.Int, "Output 1"));
		var config = Arr.create(
			new PropertyDefinition("apiKey", PropertyType.String, "API Key", IsRequired: true));

		// Act
		var schema = new ModuleSchema(inputs, outputs, config);

		// Assert
		schema.Inputs.Count().Should().Be(1);
		schema.Inputs[0].Name.Should().Be("input1");
		schema.Outputs.Count().Should().Be(1);
		schema.Outputs[0].Name.Should().Be("output1");
		schema.Configuration.Count().Should().Be(1);
		schema.Configuration[0].Name.Should().Be("apiKey");
	}

	[Fact]
	public void ModuleSchema_EmptyCollections_AreAllowed()
	{
		// Arrange & Act
		var schema = new ModuleSchema(
			Arr<PropertyDefinition>.Empty,
			Arr<PropertyDefinition>.Empty,
			Arr<PropertyDefinition>.Empty);

		// Assert
		schema.Inputs.Count().Should().Be(0);
		schema.Outputs.Count().Should().Be(0);
		schema.Configuration.Count().Should().Be(0);
	}

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

	[Fact]
	public void TriggerDefinition_Constructor_SetsAllProperties()
	{
		// Arrange
		var config = HashMap(
			("schedule", "0 0 * * *"),
			("timezone", "UTC"));

		// Act
		var trigger = new TriggerDefinition(TriggerType.Scheduled, config);

		// Assert
		trigger.Type.Should().Be(TriggerType.Scheduled);
		trigger.Configuration.Should().BeEquivalentTo(config);
	}

	[Fact]
	public void TriggerType_HasAllExpectedValues()
	{
		// Assert
		var types = Enum.GetValues<TriggerType>();
		types.Should().Contain(new[]
		{
			TriggerType.Manual,
			TriggerType.Scheduled,
			TriggerType.Webhook,
			TriggerType.Event
		});
		types.Should().HaveCount(4);
	}
}

