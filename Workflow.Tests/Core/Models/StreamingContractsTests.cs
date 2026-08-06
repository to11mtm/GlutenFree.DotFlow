// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Workflow.Api.Contracts.Modules;
using Workflow.Core.Models;
using Workflow.Engine.Serialization.JsonConverters;
using Workflow.Modules.Abstractions;

namespace Workflow.Tests.Core.Models;

/// <summary>
/// Phase 5.1.0 — tests for the streaming data-plane core contracts~ 🌊✨
/// </summary>
/// <remarks>
/// CopilotNote: These cover the contract surface only (no engine yet): StreamItem + payload union,
/// SourceOffset, streaming ports, per-connection buffer capacity, and the API projections that let
/// the designer see all of it. Backwards compatibility is asserted explicitly — every new field is
/// optional so Phase 1–4 definitions and modules keep working~ 💖.
/// </remarks>
public class StreamingContractsTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        JsonSerializerOptionsExtensions.CreateWorkflowJsonOptions();

    #region StreamItem

    [Fact]
    public void FromJson_CarriesPayloadIndexAndDefaults()
    {
        var json = JsonDocument.Parse("""{"id":7}""").RootElement;

        var item = StreamItem.FromJson(json, index: 3);

        item.Index.Should().Be(3);
        item.Offset.Should().BeNull();
        item.Payload.Should().BeOfType<JsonPayload>()
            .Which.Value.GetProperty("id").GetInt32().Should().Be(7);
    }

    [Fact]
    public void StreamItem_WithSourceOffset_KeepsTokenAndSequence()
    {
        var offset = new SourceOffset("cursor-abc", 128);

        var item = StreamItem.FromJson(JsonDocument.Parse("{}").RootElement, 0, offset);

        item.Offset.Should().Be(offset);
        item.Offset!.Token.Should().Be("cursor-abc", "the token is opaque and source-defined");
        item.Offset.Sequence.Should().Be(128, "the sequence gives every source a uniform ordering key");
    }

    [Fact]
    public void SourceOffset_HasValueEquality()
    {
        new SourceOffset("t", 1).Should().Be(new SourceOffset("t", 1));
        new SourceOffset("t", 1).Should().NotBe(new SourceOffset("t", 2));
    }

    [Fact]
    public void EmptyPayload_IsSharedAndValueEqual()
    {
        StreamPayload.Empty.Should().BeSameAs(StreamPayload.Empty);
        StreamPayload.Empty.Should().Be(new EmptyPayload());
    }

    /// <summary>
    /// 🛡️ D25 guard — the tombstone machinery was deleted when the resequencer was. If someone
    /// reintroduces it, this test's name explains why they shouldn't~ ✨
    /// </summary>
    [Fact]
    public void StreamItem_HasNoTombstoneMachinery()
    {
        typeof(StreamItem).GetProperty("IsTombstone").Should().BeNull(
            "D25 removed the resequencer, so dropped items need no marker — Akka.Streams orders by input slot");
        typeof(StreamItem).GetMethod("Tombstone").Should().BeNull();
    }

    #endregion

    #region Streaming ports

    [Fact]
    public void CreateStreaming_MarksPortStreamingAndCarriesStreamItem()
    {
        var port = PortDefinition.CreateStreaming("items", description: "the item stream");

        port.IsStreaming.Should().BeTrue();
        port.DataType.Should().Be<StreamItem>();
        port.Description.Should().Be("the item stream");
        port.SupportsTemplates.Should().BeFalse("streaming inputs are upstream data, never authored text");
    }

    [Fact]
    public void PortDefinition_DefaultsToNonStreaming()
    {
        PortDefinition.Create<string>("input").IsStreaming.Should().BeFalse();
        new PortDefinition("input", "Input", typeof(string)).IsStreaming.Should().BeFalse();
    }

    [Fact]
    public void PortDefinition_KeepsStructuralEqualityAcrossStreamingFlag()
    {
        var batch = PortDefinition.Create<string>("p");
        var streaming = PortDefinition.CreateStreaming("p");

        batch.Should().NotBe(streaming, "the shape rule depends on this flag being part of identity");
        PortDefinition.CreateStreaming("p").Should().Be(streaming);
    }

    #endregion

    #region Connection buffer capacity

    [Fact]
    public void ConnectionDefinition_BufferCapacityDefaultsToNull()
    {
        var connection = new ConnectionDefinition("a", "out", "b", "in");

        connection.BufferCapacity.Should().BeNull("null means 'use the workflow, then engine, default'");
    }

    [Fact]
    public void ConnectionDefinition_RoundTripsBufferCapacity()
    {
        var connection = new ConnectionDefinition("a", "items", "b", "items", BufferCapacity: 256);

        var json = JsonSerializer.Serialize(connection, JsonOptions);
        var restored = JsonSerializer.Deserialize<ConnectionDefinition>(json, JsonOptions);

        restored.Should().Be(connection);
        restored!.BufferCapacity.Should().Be(256);
    }

    [Fact]
    public void ConnectionDefinition_WithoutCapacity_RoundTripsUnchanged()
    {
        var connection = new ConnectionDefinition("a", "out", "b", "in", Condition: "x > 1", Priority: 2);

        var restored = JsonSerializer.Deserialize<ConnectionDefinition>(
            JsonSerializer.Serialize(connection, JsonOptions), JsonOptions);

        restored.Should().Be(connection, "pre-5.1 connections must survive the new optional field");
        restored!.BufferCapacity.Should().BeNull();
    }

    [Fact]
    public void WorkflowDefinition_WithStreamingConnection_RoundTrips()
    {
        var definition = new WorkflowDefinition(
            Guid.NewGuid(),
            "Streaming",
            null,
            new Version(1, 0, 0),
            Arr.create(new NodeDefinition("n1", "builtin.database.query", "Read", HashMap<string, JsonElement>.Empty)),
            Arr.create(new ConnectionDefinition("n1", "items", "n2", "items", BufferCapacity: 32)),
            HashMap<string, VariableDefinition>.Empty);

        var restored = JsonSerializer.Deserialize<WorkflowDefinition>(
            JsonSerializer.Serialize(definition, JsonOptions), JsonOptions);

        restored!.Connections[0].BufferCapacity.Should().Be(32);
    }

    #endregion

    #region API projections (what the designer sees)

    [Fact]
    public void PortDefinitionDto_ProjectsStreamingFlag()
    {
        PortDefinitionDto.From(PortDefinition.CreateStreaming("items")).IsStreaming.Should().BeTrue();
        PortDefinitionDto.From(PortDefinition.Create<string>("input")).IsStreaming.Should().BeFalse();
    }

    [Fact]
    public void ModuleDetailsDto_FlagsStreamCapableModulesWithCardinality()
    {
        var details = ModuleDetailsDto.From(new FakeStreamingModule());

        details.StreamCapable.Should().BeTrue();
        details.Cardinality.Should().Be(nameof(StreamCardinality.OneToOne));
        details.Schema.Outputs.Should().ContainSingle(p => p.Name == "items" && p.IsStreaming);
    }

    [Fact]
    public void ModuleDetailsDto_BatchModuleIsNotStreamCapable()
    {
        var details = ModuleDetailsDto.From(new FakeBatchModule());

        details.StreamCapable.Should().BeFalse();
        details.Cardinality.Should().BeNull();
    }

    [Fact]
    public void ModuleDetailsDto_SerializesStreamingFieldsForTheClient()
    {
        var json = JsonSerializer.Serialize(ModuleDetailsDto.From(new FakeStreamingModule()), JsonOptions);

        using var parsed = JsonDocument.Parse(json);
        parsed.RootElement.GetProperty("streamCapable").GetBoolean().Should().BeTrue();
        parsed.RootElement.GetProperty("cardinality").GetString().Should().Be("OneToOne");
    }

    #endregion

    #region Cardinality

    [Fact]
    public void Cardinality_DefaultsToOneToOne()
        => ((IStreamingWorkflowModule)new FakeStreamingModule()).Cardinality.Should().Be(StreamCardinality.OneToOne);

    [Fact]
    public void Cardinality_CanBeDeclaredVariable()
        => ((IStreamingWorkflowModule)new FakeVariableCardinalityModule()).Cardinality.Should().Be(
            StreamCardinality.Variable,
            "filters and splitters cannot be resequenced");

    #endregion

    #region Fakes

    private sealed class FakeBatchModule : IWorkflowModule
    {
        public string ModuleId => "test.batch";

        public string DisplayName => "Batch";

        public string Category => "Test";

        public string Description => "A plain batch module";

        public string Icon => "📦";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input")),
            Arr.create(PortDefinition.Create<object>("output")),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }

    private class FakeStreamingModule : IStreamingWorkflowModule
    {
        public string ModuleId => "test.streaming";

        public string DisplayName => "Streaming";

        public string Category => "Test";

        public string Description => "A streaming source";

        public string Icon => "🌊";

        public Version Version => new(1, 0, 0);

        public ModuleSchema Schema => new(
            Arr<PortDefinition>.Empty,
            Arr.create(PortDefinition.CreateStreaming("items")),
            Arr<ModulePropertyDefinition>.Empty);

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));

        public async IAsyncEnumerable<StreamItem> ExecuteStreamAsync(
            ModuleExecutionContext context,
            IAsyncEnumerable<StreamItem> input,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return StreamItem.FromJson(JsonDocument.Parse("{}").RootElement);
        }
    }

    private sealed class FakeVariableCardinalityModule : FakeStreamingModule, IStreamingWorkflowModule
    {
        StreamCardinality IStreamingWorkflowModule.Cardinality => StreamCardinality.Variable;
    }

    #endregion
}
