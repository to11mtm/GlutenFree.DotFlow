// <copyright file="GlobalVariablePickerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// 🌍 Phase 3.5 (V6) — global variables in the token picker. Globals became referenceable at run
/// time in V3, but the designer had no way to show that they exist.
/// </summary>
public class GlobalVariablePickerTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static DesignerDocument DocWithNode()
    {
        var doc = new DesignerDocument { Name = "wf" };
        doc.Nodes.Add(new DesignerNode { Id = "n1", ModuleId = "m", Name = "N" });
        return doc;
    }

    [Fact]
    public void Globals_AppearAsTheirOwnGroup()
    {
        var doc = DocWithNode();

        var options = VariableTokens.OptionsFor(doc, "n1", new[] { "apiBaseUrl" });

        var global = options.Single(o => o.Category == VariableTokens.GlobalsCategory);
        global.Token.Should().Be("{{Variable.apiBaseUrl}}");
        global.Label.Should().Be("apiBaseUrl");
        global.Detail.Should().Contain("Shared across every workflow");
    }

    [Fact]
    public void NoGlobals_MeansNoGroup()
    {
        var doc = DocWithNode();

        VariableTokens.OptionsFor(doc, "n1", globals: null)
            .Should().NotContain(o => o.Category == VariableTokens.GlobalsCategory);
        VariableTokens.OptionsFor(doc, "n1", globals: new List<string>())
            .Should().NotContain(o => o.Category == VariableTokens.GlobalsCategory);
    }

    [Fact]
    public void GlobalShadowedByAWorkflowDeclaration_IsListedOnlyOnce()
    {
        // The workflow's own declaration layers above the global (V3's precedence chain), so
        // listing both would offer the same token twice and imply a choice that doesn't exist.
        var doc = DocWithNode();
        doc.Variables["apiBaseUrl"] = Json("""{ "name": "apiBaseUrl", "type": 0 }""");

        var options = VariableTokens.OptionsFor(doc, "n1", new[] { "apiBaseUrl" });

        options.Count(o => o.Token == "{{Variable.apiBaseUrl}}").Should().Be(1);
        options.Should().NotContain(o => o.Category == VariableTokens.GlobalsCategory);
    }

    [Fact]
    public void Shadowing_IsCaseInsensitive_MatchingTheBinder()
    {
        var doc = DocWithNode();
        doc.Variables["ApiBaseUrl"] = Json("""{ "name": "ApiBaseUrl", "type": 0 }""");

        VariableTokens.OptionsFor(doc, "n1", new[] { "apibaseurl" })
            .Should().NotContain(o => o.Category == VariableTokens.GlobalsCategory);
    }

    [Fact]
    public void Globals_AreSortedAndCoexistWithTheOtherGroups()
    {
        var doc = DocWithNode();
        doc.Variables["local"] = Json("""{ "name": "local", "type": 0 }""");
        doc.Nodes.Add(new DesignerNode { Id = "up1", ModuleId = "m", Name = "Up" });
        doc.Connections.Add(new DesignerConnection
        {
            SourceNodeId = "up1", SourcePortName = "output", TargetNodeId = "n1", TargetPortName = "input",
        });

        var options = VariableTokens.OptionsFor(doc, "n1", new[] { "zeta", "alpha" });

        options.Where(o => o.Category == VariableTokens.GlobalsCategory).Select(o => o.Label)
            .Should().ContainInOrder("alpha", "zeta");
        options.Should().Contain(o => o.Category == "Variables");
        options.Should().Contain(o => o.Category == "Upstream outputs");
    }

    [Fact]
    public void TheInterimCredentialWarning_IsBlunt()
    {
        // V3.5 / Q16 — globals shipped ahead of secret support. The warning must say plainly that
        // values are readable, not hint at it.
        VariableTokens.GlobalsCredentialWarning.Should().Contain("Not for credentials yet");
        VariableTokens.GlobalsCredentialWarning.Should().Contain("plaintext");
    }
}
