// <copyright file="RunAfterConnectionTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.State;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Workflow.UI.Client.Api.Dtos;
using Workflow.UI.Client.Designer.State;
using Xunit;

/// <summary>
/// Plan 09 — the designer carries a connection's <c>kind</c> through load, save, clone and paste, so
/// saving a workflow never turns a run-after edge into a data edge with no port.
/// </summary>
public sealed class RunAfterConnectionTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Kind_SurvivesLoadAndSave()
    {
        var dto = new ConnectionDto("a", "out", "b", string.Empty, Kind: "RunAfter");

        var saved = DesignerConnection.FromDto(dto).ToDto();

        saved.Kind.Should().Be("RunAfter");
        DesignerConnection.FromDto(dto).IsRunAfter.Should().BeTrue();
    }

    [Fact]
    public void ADataEdge_IsSavedWithoutKind()
    {
        var json = JsonSerializer.Serialize(new DesignerConnection { SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", TargetPortName = "in" }.ToDto(), Web);

        json.Should().NotContain("kind", "the server's enum doesn't take null, and data is the default");
    }

    [Fact]
    public void Kind_SurvivesCloneAndPaste()
    {
        var doc = new DesignerDocument();
        doc.Nodes.Add(new DesignerNode { Id = "a", ModuleId = "m" });
        doc.Nodes.Add(new DesignerNode { Id = "b", ModuleId = "m" });
        doc.Connections.Add(new DesignerConnection { SourceNodeId = "a", SourcePortName = "out", TargetNodeId = "b", Kind = "RunAfter", BufferCapacity = 8 });

        doc.Connections[0].Clone().Kind.Should().Be("RunAfter");

        var clipboard = new DesignerClipboard();
        clipboard.Copy(doc, new[] { "a", "b" });
        var (_, pasted) = clipboard.BuildPaste(new HashSet<string> { "a", "b" });

        pasted.Should().ContainSingle();
        pasted.Single().Kind.Should().Be("RunAfter");
        pasted.Single().BufferCapacity.Should().Be(8);
    }
}
