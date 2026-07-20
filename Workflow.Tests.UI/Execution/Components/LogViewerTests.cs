// <copyright file="LogViewerTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Execution.Components;

using System;
using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Workflow.UI.Client.Execution.Components;
using Workflow.UI.Client.Execution.State;
using Xunit;

/// <summary>
/// 📜 Phase 3.5.4 — bUnit tests for the run-log viewer (level filter, search, copy/download)~ ✨.
/// </summary>
public sealed class LogViewerTests : TestContext
{
    public LogViewerTests() => this.JSInterop.Mode = JSRuntimeMode.Loose;

    private static IReadOnlyList<RunLogEntry> Sample()
    {
        var t = DateTimeOffset.UtcNow;
        return new List<RunLogEntry>
        {
            new(t, "node validate started"),
            new(t.AddMilliseconds(8), "node validate completed (8ms)"),
            new(t.AddMilliseconds(50), "node enrich failed: TimeoutError"),
        };
    }

    private IRenderedComponent<LogViewer> Render()
        => this.RenderComponent<LogViewer>(p => p.Add(x => x.Entries, Sample()));

    [Fact]
    public void Logs_RendersAll_ByDefault()
    {
        var cut = this.Render();
        cut.FindAll("[data-testid=log-line]").Should().HaveCount(3);
    }

    [Fact]
    public void Logs_LevelFilter_Filters()
    {
        var cut = this.Render();

        cut.Find("[data-testid=log-level]").Change("Error");

        var lines = cut.FindAll("[data-testid=log-line]");
        lines.Should().ContainSingle();
        lines[0].TextContent.Should().Contain("TimeoutError");
    }

    [Fact]
    public void Logs_Search_Filters()
    {
        var cut = this.Render();

        cut.Find("[data-testid=log-search]").Input("enrich");

        cut.FindAll("[data-testid=log-line]").Should().ContainSingle();
    }

    [Fact]
    public void Logs_Copy_InvokesClipboard()
    {
        var cut = this.Render();

        cut.Find("[data-testid=log-copy]").Click();

        this.JSInterop.VerifyInvoke("navigator.clipboard.writeText");
    }

    [Fact]
    public void Logs_Download_InvokesHelper()
    {
        var cut = this.Render();

        cut.Find("[data-testid=log-download]").Click();

        this.JSInterop.VerifyInvoke("dotflowDownload");
    }
}
