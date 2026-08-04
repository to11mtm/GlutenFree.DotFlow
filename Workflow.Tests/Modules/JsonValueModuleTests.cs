// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Tests.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// 🧪 Specs for <see cref="JsonValueModule"/> (<c>builtin.json.value</c>) — a configured JSON body
/// emitted as the <c>value</c> output, for demos, tests, and stubs~ 🧾.
/// </summary>
public sealed class JsonValueModuleTests
{
    private readonly JsonValueModule _module = new();

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static ModuleExecutionContext Context(object? json)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = json is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["json"] = json },
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = new EmptyServiceProvider(),
            ExecutionId = Guid.NewGuid(),
            NodeId = "json-1",
        };

    // ── Metadata & schema ────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_IsCorrect()
    {
        _module.ModuleId.Should().Be("builtin.json.value");
        _module.DisplayName.Should().Be("JSON Value");
        _module.Category.Should().Be("Utilities");
        _module.Icon.Should().Be("🧾");
        _module.Version.Should().Be(new Version(1, 0, 0));
    }

    [Fact]
    public void Schema_HasActivationInput_ValueOutput_TemplatedJsonProperty()
    {
        _module.Schema.Inputs.ToList().Should().ContainSingle(p => p.Name == "input" && !p.IsRequired);
        _module.Schema.Outputs.ToList().Should().ContainSingle(p => p.Name == "value");

        var json = _module.Schema.Properties.ToList().Should().ContainSingle(p => p.Name == "json").Subject;
        json.IsRequired.Should().BeTrue();
        json.SupportsTemplates.Should().BeTrue("so {{Variable.x}} / {{input.path}} can splice into the body~ 🔗");
    }

    // ── Execution ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmitsObject()
    {
        var result = await _module.ExecuteAsync(Context("""{ "Foo": 1, "Bar": "hello" }"""));

        result.Success.Should().BeTrue();
        var value = result.Outputs["value"].Should().BeOfType<JsonElement>().Which;
        value.ValueKind.Should().Be(JsonValueKind.Object);
        value.GetProperty("Foo").GetInt32().Should().Be(1);
        value.GetProperty("Bar").GetString().Should().Be("hello");
    }

    [Fact]
    public async Task EmitsArraysAndScalars()
    {
        (await _module.ExecuteAsync(Context("[1, 2, 3]"))).Outputs["value"]
            .Should().BeOfType<JsonElement>().Which.ValueKind.Should().Be(JsonValueKind.Array);
        (await _module.ExecuteAsync(Context("42"))).Outputs["value"]
            .Should().BeOfType<JsonElement>().Which.GetInt32().Should().Be(42);
        (await _module.ExecuteAsync(Context("null"))).Outputs["value"]
            .Should().BeOfType<JsonElement>().Which.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PreParsedElement_FromTheJsonEditor_IsEmittedAsIs()
    {
        var element = JsonDocument.Parse("""{ "a": true }""").RootElement.Clone();

        var result = await _module.ExecuteAsync(Context(element));

        result.Success.Should().BeTrue();
        result.Outputs["value"].Should().BeOfType<JsonElement>()
            .Which.GetProperty("a").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task MissingJson_Fails()
    {
        var result = await _module.ExecuteAsync(Context(null));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public async Task InvalidJson_FailsWithTemplateHint()
    {
        // Save-time validation skips templated bodies, so a template resolving into broken JSON
        // surfaces here — the message says so.
        var result = await _module.ExecuteAsync(Context("{ not json"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("template resolution");
    }

    // ── Configuration validation ─────────────────────────────────────────────────

    [Fact]
    public void Validate_MissingJson_Fails()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?>());

        result.IsValid.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle(e => e.Code == "MISSING_JSON");
    }

    [Fact]
    public void Validate_MalformedJson_FailsAtSaveTime()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?> { ["json"] = "{ nope" });

        result.IsValid.Should().BeFalse();
        result.Errors.ToList().Should().ContainSingle(e => e.Code == "INVALID_JSON");
    }

    [Fact]
    public void Validate_TemplatedJson_IsSkipped_JudgedAtRunTime()
    {
        var result = _module.ValidateConfiguration(new Dictionary<string, object?>
        {
            ["json"] = """{ "id": {{input.Thing.Id}} }""",
        });

        result.IsValid.Should().BeTrue("templates can't be judged until they're bound~ 🔗");
    }

    [Fact]
    public void Validate_WellFormedJson_Passes()
    {
        _module.ValidateConfiguration(new Dictionary<string, object?> { ["json"] = """{ "ok": true }""" })
            .IsValid.Should().BeTrue();
    }
}
