// <copyright file="DocModelTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Modules.State;
using Xunit;

/// <summary>
/// 📖 Phase 3.6.1 — Tests for the framework-free <see cref="ModuleDocModel"/> generator~ ✨.
/// </summary>
public sealed class DocModelTests
{
    private static JsonElement El(string j) => JsonDocument.Parse(j).RootElement.Clone();

    private static ModuleDetailsDto Details()
        => new(
            "builtin.script",
            "Script",
            "Scripting",
            "Runs sandboxed code",
            "📜",
            "1.0.0",
            new ModuleSchemaDto(
                new List<PortDefinitionDto>
                {
                    new("input", "Input", "object", "The upstream payload", true, null),
                },
                new List<PortDefinitionDto>
                {
                    new("result", "Result", null, null, false, null),
                },
                new List<ModulePropertyDefinitionDto>
                {
                    new("language", "Language", "string", "Script language", true, El("\"javascript\""), "Select", new List<JsonElement> { El("\"javascript\""), El("\"lua\"") }),
                    new("code", "Code", "string", null, true, null, "Code", null),
                }),
            new List<string> { "builtin.log" },
            true,
            new List<string> { "1.0.0", "0.9.0" });

    [Fact]
    public void DocModel_Ports_Projected_WithRequiredAndType()
    {
        var m = ModuleDocModel.From(Details());

        m.Inputs.Should().ContainSingle();
        m.Inputs[0].Type.Should().Be("object");
        m.Inputs[0].Required.Should().BeTrue();
        m.Inputs[0].Description.Should().Be("The upstream payload");

        // A null DataType renders as "any".
        m.Outputs[0].Type.Should().Be("any");
        m.Outputs[0].Required.Should().BeFalse();
    }

    [Fact]
    public void DocModel_Properties_IncludeEditorAllowedAndDefault()
    {
        var m = ModuleDocModel.From(Details());

        var lang = m.Properties.Single(p => p.Name == "language");
        lang.Editor.Should().Be("Select");
        lang.Required.Should().BeTrue();
        lang.Default.Should().Be("javascript");
        lang.Allowed.Should().ContainInOrder("javascript", "lua");

        var code = m.Properties.Single(p => p.Name == "code");
        code.Editor.Should().Be("Code");
        code.Allowed.Should().BeEmpty();
        code.Default.Should().BeNull();
    }

    [Fact]
    public void DocModel_Versions_FlagActive()
    {
        var m = ModuleDocModel.From(Details());

        m.Versions.Should().HaveCount(2);
        m.Versions.Single(v => v.Version == "1.0.0").Active.Should().BeTrue();
        m.Versions.Single(v => v.Version == "0.9.0").Active.Should().BeFalse();
    }

    [Fact]
    public void DocModel_Dependencies_Projected()
        => ModuleDocModel.From(Details()).Dependencies.Should().ContainSingle(d => d == "builtin.log");

    [Fact]
    public void DocModel_NoDeps_NoVersions_EmptySections()
    {
        var d = Details() with { Dependencies = new List<string>(), AvailableVersions = null };
        var m = ModuleDocModel.From(d);
        m.Dependencies.Should().BeEmpty();
        m.Versions.Should().BeEmpty();
    }
}
