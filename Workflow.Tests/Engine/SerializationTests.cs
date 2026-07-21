// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using MessagePack;
using MessagePack.Resolvers;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Engine.Serialization;
using Workflow.Engine.Serialization.JsonConverters;
using static LanguageExt.Prelude;

namespace Workflow.Tests.Engine;

/// <summary>
/// Tests for serialization of LanguageExt types and workflow messages.
/// Covers both System.Text.Json (for APIs) and MessagePack (for Akka.NET)~ 🧪✨
/// </summary>
/// <remarks>
/// CopilotNote: These tests verify that:
/// - HashMap, Option, and Arr serialize/deserialize correctly with JSON.
/// - MessagePack works out of the box for Akka.NET persistence.
/// - All workflow message types can round-trip successfully.
/// </remarks>
public class SerializationTests
{
    #region JSON Options Setup

    /// <summary>
    /// Pre-configured JsonSerializerOptions with all LanguageExt converters~ 🎀
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions =
        JsonSerializerOptionsExtensions.CreateWorkflowJsonOptions(writeIndented: true);

    /// <summary>
    /// MessagePack options matching the Akka.NET serializer configuration~ 📦
    /// </summary>
    private static readonly MessagePackSerializerOptions _messagePackOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                MyResolver.Instance,
                ContractlessStandardResolver.Instance,
                StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData);

    #endregion

    #region HashMap JSON Tests 🗺️

    /// <summary>
    /// Tests that HashMap with string keys and object values serializes to JSON object format.
    /// </summary>
    [Fact]
    public void HashMapStringObjectSerializesToJsonObject()
    {
        // Arrange - Create a HashMap with various value types 🎨
        var hashMap = HashMap(
            ("name", (object?)"Ami-Chan"),
            ("age", (object?)25),
            ("isKawaii", (object?)true),
            ("nullValue", (object?)null));

        // Act - Serialize to JSON 📝
        var json = JsonSerializer.Serialize(hashMap, _jsonOptions);

        // Assert - Should be a proper JSON object, not an array! 💖
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"Ami-Chan\"");
        json.Should().Contain("\"age\"");
        json.Should().Contain("\"isKawaii\"");
        json.Should().StartWith("{");
        json.Should().EndWith("}");
    }

    /// <summary>
    /// Tests that HashMap deserializes correctly from JSON object format.
    /// </summary>
    [Fact]
    public void HashMapStringStringRoundTripsCorrectly()
    {
        // Arrange 🎀
        var original = HashMap(
            ("key1", "value1"),
            ("key2", "value2"),
            ("key3", "value3"));

        // Act - Round trip through JSON 🔄
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<HashMap<string, string>>(json, _jsonOptions);

        // Assert - Should match original! 💕
        deserialized.Count.Should().Be(3);
        deserialized["key1"].Should().Be("value1");
        deserialized["key2"].Should().Be("value2");
        deserialized["key3"].Should().Be("value3");
    }

    /// <summary>
    /// Tests that empty HashMap serializes to empty JSON object.
    /// </summary>
    [Fact]
    public void HashMapEmptySerializesToEmptyObject()
    {
        // Arrange
        HashMap<string, int> empty = HashMap<string, int>();

        // Act
        var json = JsonSerializer.Serialize(empty, _jsonOptions);

        // Assert
        json.Should().Be("{}");
    }

    /// <summary>
    /// Tests HashMap with Guid keys for workflow IDs and such.
    /// </summary>
    [Fact]
    public void HashMapGuidKeyRoundTripsCorrectly()
    {
        // Arrange 🆔
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var original = HashMap(
            (id1, "workflow1"),
            (id2, "workflow2"));

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<HashMap<Guid, string>>(json, _jsonOptions);

        // Assert
        deserialized.Count.Should().Be(2);
        deserialized[id1].Should().Be("workflow1");
        deserialized[id2].Should().Be("workflow2");
    }

    #endregion

    #region Option JSON Tests 💫

    /// <summary>
    /// Tests that Option.Some serializes as the value itself.
    /// </summary>
    [Fact]
    public void OptionSomeSerializesAsValue()
    {
        // Arrange
        var some = Some("Hello UwU!");

        // Act
        var json = JsonSerializer.Serialize(some, _jsonOptions);

        // Assert - Should be just the string, not an array! 🎀
        json.Should().Be("\"Hello UwU!\"");
    }

    /// <summary>
    /// Tests that Option.None serializes as null.
    /// </summary>
    [Fact]
    public void OptionNoneSerializesAsNull()
    {
        // Arrange
        var none = Option<string>.None;

        // Act
        var json = JsonSerializer.Serialize(none, _jsonOptions);

        // Assert 🚫
        json.Should().Be("null");
    }

    /// <summary>
    /// Tests Option with complex type round-trips correctly.
    /// </summary>
    [Fact]
    public void OptionComplexTypeRoundTripsCorrectly()
    {
        // Arrange 💕
        var original = Some(new TestData("Ami", 25));

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Option<TestData>>(json, _jsonOptions);

        // Assert
        deserialized.IsSome.Should().BeTrue();
        deserialized.IfNone(new TestData("", 0)).Name.Should().Be("Ami");
    }

    /// <summary>
    /// Tests Option.None deserialization from null.
    /// </summary>
    [Fact]
    public void OptionFromNullDeserializesToNone()
    {
        // Arrange
        var json = "null";

        // Act
        var deserialized = JsonSerializer.Deserialize<Option<string>>(json, _jsonOptions);

        // Assert
        deserialized.IsNone.Should().BeTrue();
    }

    /// <summary>
    /// Tests Option with DateTimeOffset (common in workflow status).
    /// </summary>
    [Fact]
    public void OptionDateTimeOffsetRoundTripsCorrectly()
    {
        // Arrange 📅
        var now = DateTimeOffset.UtcNow;
        var original = Some(now);

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Option<DateTimeOffset>>(json, _jsonOptions);

        // Assert
        deserialized.IsSome.Should().BeTrue();
        deserialized.IfNone(DateTimeOffset.MinValue).Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Arr JSON Tests 📚

    /// <summary>
    /// Tests that Arr serializes to JSON array format.
    /// </summary>
    [Fact]
    public void ArrSerializesToJsonArray()
    {
        // Arrange 🎨
        var arr = Array("apple", "banana", "cherry");

        // Act
        var json = JsonSerializer.Serialize(arr, _jsonOptions);

        // Assert
        json.Should().Contain("[");
        json.Should().Contain("]");
        json.Should().Contain("\"apple\"");
        json.Should().Contain("\"banana\"");
        json.Should().Contain("\"cherry\"");
    }

    /// <summary>
    /// Tests that Arr deserializes correctly from JSON array.
    /// </summary>
    [Fact]
    public void ArrRoundTripsCorrectly()
    {
        // Arrange 💖
        var original = Array(1, 2, 3, 4, 5);

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Arr<int>>(json, _jsonOptions);

        // Assert
        deserialized.Count.Should().Be(5);
        deserialized[0].Should().Be(1);
        deserialized[4].Should().Be(5);
    }

    /// <summary>
    /// Tests that empty Arr serializes to empty JSON array.
    /// </summary>
    [Fact]
    public void ArrEmptySerializesToEmptyArray()
    {
        // Arrange
        Arr<string> empty = new Arr<string>();

        // Act
        var json = JsonSerializer.Serialize(empty, _jsonOptions);

        // Assert
        json.Should().Be("[]");
    }

    /// <summary>
    /// Tests Arr with complex objects.
    /// </summary>
    [Fact]
    public void ArrComplexTypeRoundTripsCorrectly()
    {
        // Arrange 🎀
        var original = Array(
            new TestData("Ami", 25),
            new TestData("Senpai", 30));

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Arr<TestData>>(json, _jsonOptions);

        // Assert
        deserialized.Count.Should().Be(2);
        deserialized[0].Name.Should().Be("Ami");
        deserialized[1].Age.Should().Be(30);
    }

    #endregion

    #region Nested LanguageExt Types 🎭

    /// <summary>
    /// Tests HashMap containing Option values.
    /// </summary>
    [Fact]
    public void HashMapWithOptionValuesRoundTripsCorrectly()
    {
        // Arrange - This is a common pattern in workflow state! 💕
        var original = HashMap(
            ("present", Some(42)),
            ("missing", Option<int>.None));

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<HashMap<string, Option<int>>>(json, _jsonOptions);

        // Assert
        deserialized["present"].IsSome.Should().BeTrue();
        deserialized["present"].IfNone(0).Should().Be(42);
        deserialized["missing"].IsNone.Should().BeTrue();
    }

    /// <summary>
    /// Tests Arr containing Option values.
    /// </summary>
    [Fact]
    public void ArrWithOptionValuesRoundTripsCorrectly()
    {
        // Arrange
        var original = Array(
            Some("value1"),
            Option<string>.None,
            Some("value3"));

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<Arr<Option<string>>>(json, _jsonOptions);

        // Assert
        deserialized.Count.Should().Be(3);
        deserialized[0].IsSome.Should().BeTrue();
        deserialized[1].IsNone.Should().BeTrue();
        deserialized[2].IsSome.Should().BeTrue();
    }

    #endregion

    #region MessagePack Tests 📦

    /// <summary>
    /// Tests that HashMap serializes/deserializes with MessagePack.
    /// </summary>
    [Fact]
    public void HashMapMessagePackRoundTripsCorrectly()
    {
        // Arrange 🗺️
        var original = HashMap(
            ("name", (object?)"Ami"),
            ("count", (object?)42));

        // Act
        var bytes = MessagePackSerializer.Serialize(original, _messagePackOptions);
        var deserialized = MessagePackSerializer.Deserialize<HashMap<string, object?>>(bytes, _messagePackOptions);

        // Assert
        deserialized.Count.Should().Be(2);
    }

    /// <summary>
    /// Tests that Arr serializes/deserializes with MessagePack.
    /// </summary>
    [Fact]
    public void ArrMessagePackRoundTripsCorrectly()
    {
        // Arrange 📚
        var original = Array("one", "two", "three");

        // Act
        var bytes = MessagePackSerializer.Serialize(original, _messagePackOptions);
        var deserialized = MessagePackSerializer.Deserialize<Arr<string>>(bytes, _messagePackOptions);

        // Assert
        deserialized.Count.Should().Be(3);
        deserialized[0].Should().Be("one");
    }

    /// <summary>
    /// Tests that Option serializes/deserializes with MessagePack.
    /// </summary>
    [Fact]
    public void OptionMessagePackRoundTripsCorrectly()
    {
        // Arrange 💫
        var someDateOffset = Some(DateTimeOffset.UtcNow);
        var noneString = Option<string>.None;

        // Act & Assert - Some
        var someBytes = MessagePackSerializer.Serialize(someDateOffset, _messagePackOptions);
        var someDeserialized = MessagePackSerializer.Deserialize<Option<DateTimeOffset>>(someBytes, _messagePackOptions);
        someDeserialized.IsSome.Should().BeTrue();

        // Act & Assert - None
        var noneBytes = MessagePackSerializer.Serialize(noneString, _messagePackOptions);
        var noneDeserialized = MessagePackSerializer.Deserialize<Option<string>>(noneBytes, _messagePackOptions);
        noneDeserialized.IsNone.Should().BeTrue();
    }

    #endregion

    #region Workflow Message Tests 💌

    /// <summary>
    /// Tests that CreateWorkflowInstance message round-trips with MessagePack.
    /// </summary>
    [Fact]
    public void CreateWorkflowInstanceMessagePackRoundTripsCorrectly()
    {
        // Arrange 🚀
        var workflowId = Guid.NewGuid();
        var definition = CreateTestWorkflowDefinition();
        var inputs = HashMap(("input1", (object?)"value1"));

        var original = new CreateWorkflowInstance(workflowId, definition, inputs);

        // Act
        var bytes = MessagePackSerializer.Serialize(original, _messagePackOptions);
        var deserialized = MessagePackSerializer.Deserialize<CreateWorkflowInstance>(bytes, _messagePackOptions);

        // Assert 💖
        deserialized.WorkflowId.Should().Be(workflowId);
        deserialized.Definition.Name.Should().Be("Test Workflow");
        deserialized.Inputs.Count.Should().Be(1);
    }

    /// <summary>
    /// Tests that WorkflowStatusResponse with all fields round-trips correctly.
    /// </summary>
    [Fact]
    public void WorkflowStatusResponseMessagePackRoundTripsCorrectly()
    {
        // Arrange 📊
        var executionId = Guid.NewGuid();
        var nodeStates = HashMap(
            ("node1", NodeExecutionState.Completed),
            ("node2", NodeExecutionState.Running));

        var original = new WorkflowStatusResponse(
            executionId,
            ExecutionState.Running,
            Progress: 50,
            nodeStates,
            StartTime: DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime: None,
            Error: None);

        // Act
        var bytes = MessagePackSerializer.Serialize(original, _messagePackOptions);
        var deserialized = MessagePackSerializer.Deserialize<WorkflowStatusResponse>(bytes, _messagePackOptions);

        // Assert 💕
        deserialized.ExecutionId.Should().Be(executionId);
        deserialized.State.Should().Be(ExecutionState.Running);
        deserialized.Progress.Should().Be(50);
        deserialized.NodeStates.Count.Should().Be(2);
        deserialized.EndTime.IsNone.Should().BeTrue();
        deserialized.Error.IsNone.Should().BeTrue();
    }

    /// <summary>
    /// Tests that WorkflowInstanceCreationFailed with Arr of errors round-trips.
    /// </summary>
    [Fact]
    public void WorkflowInstanceCreationFailedMessagePackRoundTripsCorrectly()
    {
        // Arrange ❌
        var workflowId = Guid.NewGuid();
        var errors = Array("Error 1: Invalid node", "Error 2: Missing connection");

        var original = new WorkflowInstanceCreationFailed(workflowId, errors);

        // Act
        var bytes = MessagePackSerializer.Serialize(original, _messagePackOptions);
        var deserialized = MessagePackSerializer.Deserialize<WorkflowInstanceCreationFailed>(bytes, _messagePackOptions);

        // Assert
        deserialized.WorkflowId.Should().Be(workflowId);
        deserialized.Errors.Count.Should().Be(2);
        deserialized.Errors[0].Should().Be("Error 1: Invalid node");
    }

    /// <summary>
    /// Tests CreateWorkflowInstance message round-trips with JSON.
    /// </summary>
    [Fact]
    public void CreateWorkflowInstanceJsonRoundTripsCorrectly()
    {
        // Arrange 🌸
        var workflowId = Guid.NewGuid();
        var definition = CreateTestWorkflowDefinition();
        var inputs = HashMap(("greeting", (object?)"Hello UwU!"));

        var original = new CreateWorkflowInstance(workflowId, definition, inputs);

        // Act
        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var deserialized = JsonSerializer.Deserialize<CreateWorkflowInstance>(json, _jsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.WorkflowId.Should().Be(workflowId);
        deserialized.Definition.Name.Should().Be("Test Workflow");
    }

    #endregion

    #region Helper Types and Methods 🛠️

    /// <summary>
    /// Simple test record for complex type serialization tests.
    /// </summary>
    private record TestData(string Name, int Age);

    /// <summary>
    /// Creates a minimal workflow definition for testing.
    /// </summary>
    private static WorkflowDefinition CreateTestWorkflowDefinition()
    {
        return new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Test Workflow",
            Description: "A kawaii test workflow~ UwU",
            Version: new Version(1, 0, 0),
            Nodes: new Arr<NodeDefinition>(),
            Connections: new Arr<ConnectionDefinition>(),
            Variables: HashMap<string, VariableDefinition>());
    }

    #endregion
}
