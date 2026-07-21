// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules.Binding;
using Xunit;

/// <summary>
/// 🔗 Phase 1.4.4 — Tests for PropertyBinder!
/// Validates the full property binding pipeline: type conversion, variable resolution,
/// node output resolution, default application, error accumulation, and more. UwU ✨💖
/// </summary>
public class PropertyBinderTests
{
    private readonly PropertyBinder _binder = new();

    /// <summary>
    /// 📝 A string value passed to a string port should come through unchanged. UwU~
    /// </summary>
    [Fact]
    public void BindProperties_StringPassThrough_ShouldReturnStringAsIs()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["name"] = "Ami-chan" };
        var schema = Arr.create(PortDefinition.Create<string>("name"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["name"].Should().Be("Ami-chan");
    }

    /// <summary>
    /// 🔢 A string "42" should be converted to int when the port expects int. Sugoi~
    /// </summary>
    [Fact]
    public void BindProperties_IntConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["count"] = "42" };
        var schema = Arr.create(PortDefinition.Create<int>("count"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["count"].Should().Be(42);
    }

    /// <summary>
    /// ✅ A string "true" should be converted to bool when the port expects bool.
    /// </summary>
    [Fact]
    public void BindProperties_BoolConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["enabled"] = "true" };
        var schema = Arr.create(PortDefinition.Create<bool>("enabled"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["enabled"].Should().Be(true);
    }

    /// <summary>
    /// 📅 A string date should be converted to DateTime when the port expects it.
    /// </summary>
    [Fact]
    public void BindProperties_DateTimeConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["when"] = "2026-04-09T12:00:00Z" };
        var schema = Arr.create(PortDefinition.Create<DateTime>("when"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["when"].Should().BeOfType<DateTime>();
    }

    /// <summary>
    /// 🎲 A string GUID should be converted to Guid when the port expects it.
    /// </summary>
    [Fact]
    public void BindProperties_GuidConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var guid = Guid.NewGuid();
        var rawValues = new Dictionary<string, object?> { ["id"] = guid.ToString() };
        var schema = Arr.create(PortDefinition.Create<Guid>("id"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["id"].Should().Be(guid);
    }

    /// <summary>
    /// ⏱️ A string TimeSpan should be converted when the port expects it.
    /// </summary>
    [Fact]
    public void BindProperties_TimeSpanConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["delay"] = "00:05:30" };
        var schema = Arr.create(PortDefinition.Create<TimeSpan>("delay"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["delay"].Should().Be(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 💰 A string decimal should be converted when the port expects it.
    /// </summary>
    [Fact]
    public void BindProperties_DecimalConversionFromString_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["price"] = "99.99" };
        var schema = Arr.create(PortDefinition.Create<decimal>("price"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["price"].Should().Be(99.99m);
    }

    /// <summary>
    /// 📋 A JSON string should be deserialized into a complex object when the port expects it.
    /// </summary>
    [Fact]
    public void BindProperties_JsonStringToComplexObject_ShouldDeserialize()
    {
        // Arrange 🎀
        var json = """{"Name":"Ami","Level":99}""";
        var rawValues = new Dictionary<string, object?> { ["config"] = json };
        var schema = Arr.create(PortDefinition.Create<TestConfig>("config"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        var config = result.BoundValues["config"].Should().BeOfType<TestConfig>().Subject;
        config.Name.Should().Be("Ami");
        config.Level.Should().Be(99);
    }

    /// <summary>
    /// 💾 A <c>{{Variable.Name}}</c> reference should resolve from workflow variables.
    /// </summary>
    [Fact]
    public void BindProperties_VariableReference_ShouldResolve()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["greeting"] = "{{Variable.UserName}}" };
        var schema = Arr.create(PortDefinition.Create<string>("greeting"));
        var context = new PropertyBindingContext(
            Variables: new Dictionary<string, object?> { ["UserName"] = "Ami-chan" },
            NodeOutputs: new Dictionary<string, IReadOnlyDictionary<string, object?>>());

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, context);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["greeting"].Should().Be("Ami-chan");
    }

    /// <summary>
    /// 🔗 A <c>{{Variable.User.Name}}</c> nested reference should traverse with dot-notation.
    /// </summary>
    [Fact]
    public void BindProperties_NestedVariableReference_ShouldResolve()
    {
        // Arrange 🎀
        var userDict = new Dictionary<string, object?> { ["Name"] = "Ami-chan", ["Level"] = 99 };
        var rawValues = new Dictionary<string, object?> { ["name"] = "{{Variable.User.Name}}" };
        var schema = Arr.create(PortDefinition.Create<string>("name"));
        var context = new PropertyBindingContext(
            Variables: new Dictionary<string, object?> { ["User"] = userDict },
            NodeOutputs: new Dictionary<string, IReadOnlyDictionary<string, object?>>());

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, context);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["name"].Should().Be("Ami-chan");
    }

    /// <summary>
    /// 📤 A <c>{{NodeId.OutputName}}</c> reference should resolve from predecessor node outputs.
    /// </summary>
    [Fact]
    public void BindProperties_NodeOutputReference_ShouldResolve()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["input"] = "{{node1.result}}" };
        var schema = Arr.create(PortDefinition.Create<string>("input"));
        var nodeOutputs = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["node1"] = new Dictionary<string, object?> { ["result"] = "kawaii-output" },
        };
        var context = new PropertyBindingContext(
            Variables: new Dictionary<string, object?>(),
            NodeOutputs: nodeOutputs);

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, context);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["input"].Should().Be("kawaii-output");
    }

    /// <summary>
    /// ❌ A <c>{{Variable.Missing}}</c> reference to a non-existent variable should produce an error.
    /// </summary>
    [Fact]
    public void BindProperties_MissingVariableReference_ShouldError()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["value"] = "{{Variable.Missing}}" };
        var schema = Arr.create(PortDefinition.Create<string>("value"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ❌
        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle()
            .Which.Should().Contain("Missing");
    }

    /// <summary>
    /// ❌ A <c>{{unknownNode.output}}</c> reference to a non-existent node should produce an error.
    /// </summary>
    [Fact]
    public void BindProperties_MissingNodeOutputReference_ShouldError()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["value"] = "{{unknownNode.output}}" };
        var schema = Arr.create(PortDefinition.Create<string>("value"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ❌
        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle()
            .Which.Should().Contain("unknownNode");
    }

    /// <summary>
    /// 💫 When an optional input is missing and has a default, the default should be applied.
    /// </summary>
    [Fact]
    public void BindProperties_DefaultValueApplied_ForOptionalMissingInput()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?>(); // Empty — no inputs provided!
        var schema = Arr.create(new PortDefinition(
            Name: "timeout",
            DisplayName: "Timeout",
            DataType: typeof(int),
            IsRequired: false,
            DefaultValue: 30));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["timeout"].Should().Be(30);
    }

    /// <summary>
    /// ❌ When a required input is missing, the binder should produce an error.
    /// </summary>
    [Fact]
    public void BindProperties_RequiredMissingInput_ShouldError()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?>(); // Empty — nothing provided!
        var schema = Arr.create(PortDefinition.Create<string>("required_field"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ❌
        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle()
            .Which.Should().Contain("required_field");
    }

    /// <summary>
    /// ❌ When a value can't be converted to the expected type, the binder should produce an error.
    /// </summary>
    [Fact]
    public void BindProperties_TypeMismatchAfterConversion_ShouldError()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["count"] = "not-a-number" };
        var schema = Arr.create(PortDefinition.Create<int>("count"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ❌
        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle()
            .Which.Should().Contain("count");
    }

    /// <summary>
    /// ❌ Multiple binding errors should all be accumulated rather than short-circuiting.
    /// </summary>
    [Fact]
    public void BindProperties_MultipleErrors_ShouldAccumulateAll()
    {
        // Arrange 🎀 — Two required ports, both missing!
        var rawValues = new Dictionary<string, object?>();
        var schema = Arr.create(
            PortDefinition.Create<string>("field1"),
            PortDefinition.Create<int>("field2"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ❌
        result.Success.Should().BeFalse();
        result.Errors.ToList().Should().HaveCount(2, "both missing required fields should be reported~ uwu");
    }

    /// <summary>
    /// 🔢 An int value passed to a long port should be widened via numeric conversion.
    /// </summary>
    [Fact]
    public void BindProperties_NumericWidening_IntToLong_ShouldConvert()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["value"] = 42 };
        var schema = Arr.create(PortDefinition.Create<long>("value"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["value"].Should().Be(42L);
    }

    /// <summary>
    /// 📝 When a variable reference resolves to a non-string type AND the whole string
    /// is a single reference, the original type should be preserved (int stays int). UwU~
    /// </summary>
    [Fact]
    public void BindProperties_SingleVariableReference_PreservesResolvedType()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["count"] = "{{Variable.ItemCount}}" };
        var schema = Arr.create(PortDefinition.Create<int>("count"));
        var context = new PropertyBindingContext(
            Variables: new Dictionary<string, object?> { ["ItemCount"] = 42 },
            NodeOutputs: new Dictionary<string, IReadOnlyDictionary<string, object?>>());

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, context);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["count"].Should().Be(42);
    }

    /// <summary>
    /// 📝 Mixed text with references should interpolate as a string.
    /// E.g., "Hello {{Variable.Name}}!" → "Hello Ami-chan!". ✨
    /// </summary>
    [Fact]
    public void BindProperties_MixedTextWithReferences_ShouldInterpolateAsString()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?>
        {
            ["message"] = "Hello {{Variable.Name}}, you are level {{Variable.Level}}!",
        };
        var schema = Arr.create(PortDefinition.Create<string>("message"));
        var context = new PropertyBindingContext(
            Variables: new Dictionary<string, object?>
            {
                ["Name"] = "Ami-chan",
                ["Level"] = 99,
            },
            NodeOutputs: new Dictionary<string, IReadOnlyDictionary<string, object?>>());

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, context);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["message"].Should().Be("Hello Ami-chan, you are level 99!");
    }

    /// <summary>
    /// 🌙 Optional port with no value and no default should bind to null.
    /// </summary>
    [Fact]
    public void BindProperties_OptionalMissingNoDefault_ShouldBindToNull()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?>();
        var schema = Arr.create(new PortDefinition(
            Name: "optional_field",
            DisplayName: "Optional",
            DataType: typeof(string),
            IsRequired: false));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["optional_field"].Should().BeNull();
    }

    /// <summary>
    /// 🎁 Extra values not in the schema should be passed through for flexibility.
    /// </summary>
    [Fact]
    public void BindProperties_ExtraValues_ShouldPassThrough()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?>
        {
            ["defined"] = "hello",
            ["extra_key"] = "bonus_value",
        };
        var schema = Arr.create(PortDefinition.Create<string>("defined"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["defined"].Should().Be("hello");
        result.BoundValues["extra_key"].Should().Be("bonus_value");
    }

    /// <summary>
    /// 🔍 Case-insensitive port name matching — "NAME" should match port "name".
    /// </summary>
    [Fact]
    public void BindProperties_CaseInsensitivePortMatch_ShouldResolve()
    {
        // Arrange 🎀
        var rawValues = new Dictionary<string, object?> { ["NAME"] = "Ami-chan" };
        var schema = Arr.create(PortDefinition.Create<string>("name"));

        // Act ⚡
        var result = _binder.BindProperties(rawValues, schema, PropertyBindingContext.Empty);

        // Assert ✅
        result.Success.Should().BeTrue();
        result.BoundValues["name"].Should().Be("Ami-chan");
    }

    /// <summary>
    /// Helper class for JSON deserialization tests. 🧪
    /// </summary>
    public class TestConfig
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the level.</summary>
        public int Level { get; set; }
    }
}

