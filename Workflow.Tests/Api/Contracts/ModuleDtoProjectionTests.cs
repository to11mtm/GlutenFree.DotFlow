// <copyright file="ModuleDtoProjectionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.Contracts;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Api.Contracts.Modules;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Xunit;

/// <summary>
/// 📐 Phase 2.7.3 — Unit tests for module DTO projection (ports/properties/type/version/JSON)~ ✨.
/// </summary>
public sealed class ModuleDtoProjectionTests
{
    [Fact]
    public void Projection_MapsPortsAndProperties()
    {
        var details = ModuleDetailsDto.From(new StubModule());

        details.Id.Should().Be("test.stub");
        details.Schema.Inputs.Should().HaveCount(1);
        details.Schema.Outputs.Should().HaveCount(1);
        details.Schema.Properties.Should().HaveCount(1);

        details.Schema.Inputs[0].Name.Should().Be("in");
        details.Schema.Inputs[0].IsRequired.Should().BeTrue();
        details.Schema.Properties[0].Name.Should().Be("mode");
    }

    [Fact]
    public void Projection_TypeAndVersion_Serialize()
    {
        var details = ModuleDetailsDto.From(new StubModule());

        details.Version.Should().Be("2.1.0");
        details.Schema.Inputs[0].DataType.Should().Be("System.String");
        details.Schema.Properties[0].DataType.Should().Be("System.Int32");
        details.Schema.Properties[0].EditorType.Should().Be(nameof(PropertyEditorType.Dropdown));
        details.Schema.Properties[0].AllowedValues.Should().NotBeNull();
        details.Schema.Properties[0].AllowedValues!.Should().HaveCount(2);
    }

    [Fact]
    public void Dto_RoundTripsThroughJson()
    {
        var details = ModuleDetailsDto.From(new StubModule());

        var json = JsonSerializer.Serialize(details, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var back = JsonSerializer.Deserialize<ModuleDetailsDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        back.Should().NotBeNull();
        back!.Id.Should().Be("test.stub");
        back.Schema.Properties.Should().HaveCount(1);
        back.Dependencies.Should().ContainSingle().Which.Should().Be("builtin.passthrough");
    }

    [Fact]
    public void Summary_OmitsSchema()
    {
        var summary = ModuleSummaryDto.From(new StubModule());

        summary.Id.Should().Be("test.stub");
        summary.Category.Should().Be("Testing");
        summary.Version.Should().Be("2.1.0");
    }

    private sealed class StubModule : IWorkflowModule
    {
        public string ModuleId => "test.stub";

        public string DisplayName => "Stub Module";

        public string Category => "Testing";

        public string Description => "A stub for projection tests.";

        public string Icon => "🧪";

        public Version Version => new(2, 1, 0);

        public ModuleSchema Schema => new(
            Arr.create(new PortDefinition("in", "Input", typeof(string), "The input", IsRequired: true)),
            Arr.create(new PortDefinition("out", "Output", typeof(string), "The output", IsRequired: false)),
            Arr.create(new ModulePropertyDefinition(
                "mode",
                "Mode",
                typeof(int),
                "Operating mode",
                IsRequired: false,
                DefaultValue: 1,
                EditorType: PropertyEditorType.Dropdown,
                AllowedValues: Arr.create<object>(1, 2))));

        public IReadOnlyList<string> Dependencies => new[] { "builtin.passthrough" };

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?>()));
    }
}
