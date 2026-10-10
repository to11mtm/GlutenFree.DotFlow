// <copyright file="ValidateConfigurationFindingTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Findings.Modules;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Workflow.Core.Models;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Validation;
using Xunit;

/// <summary>
/// Finding 04 §9.4 — <c>IWorkflowModule.ValidateConfiguration</c> is never called.
/// </summary>
/// <remarks>
/// Many built-in modules implement <c>ValidateConfiguration</c>, but neither the validators nor the
/// engine invoke it, so those checks never run. This test registers a module that rejects its
/// configuration and asserts <c>ModuleAwareWorkflowValidator</c> reports the workflow invalid. Pure
/// in-memory validation; no side effects.
/// </remarks>
[Trait("Category", FindingDefs.Category)]
public sealed class ValidateConfigurationFindingTests
{
    [Fact]
    public void Validator_ShouldSurface_ModuleValidateConfigurationErrors()
    {
        var registry = new InMemoryModuleRegistry(skipValidation: true);
        registry.RegisterModule(new SelfRejectingModule("f.selfreject"));
        var validator = new ModuleAwareWorkflowValidator(registry);

        var definition = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "validate-config",
            Description: null,
            Version: new Version(1, 0),
            Nodes: Arr.create(new NodeDefinition(
                "n1",
                "f.selfreject",
                "N1",
                HashMap<string, System.Text.Json.JsonElement>.Empty.Add(
                    "mode",
                    System.Text.Json.JsonSerializer.SerializeToElement("invalid")))),
            Connections: Arr<ConnectionDefinition>.Empty,
            Variables: HashMap<string, VariableDefinition>.Empty);

        var result = validator.Validate(definition);

        result.IsValid.Should().BeFalse(
            because: "a module that rejects its own configuration should make the workflow invalid (04 §9.4)");
    }

    /// <summary>A module whose <see cref="ValidateConfiguration"/> always fails.</summary>
    private sealed class SelfRejectingModule : IWorkflowModule
    {
        public SelfRejectingModule(string moduleId) => this.ModuleId = moduleId;

        public string ModuleId { get; }

        public string DisplayName => this.ModuleId;

        public string Category => "Test";

        public string Description => "Rejects its own configuration";

        public string Icon => "🚫";

        public Version Version => new(1, 0);

        public ModuleSchema Schema => new(
            Arr.create(PortDefinition.Create<object>("input", isRequired: false)),
            Arr.create(PortDefinition.Create<object>("output", isRequired: false)),
            Arr.create(new ModulePropertyDefinition("mode", "Mode", typeof(string))));

        public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
            => ValidationResult.Failure(new ValidationError("SELF001", "configuration is not acceptable"));

        public Task<ModuleResult> ExecuteAsync(ModuleExecutionContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(ModuleResult.Ok(new Dictionary<string, object?> { ["output"] = "ok" }));
    }
}
