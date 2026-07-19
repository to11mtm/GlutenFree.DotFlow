// <copyright file="ScriptTestHarness.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Api;

/// <summary>
/// 🧪 Phase 3.1 — Shared helpers for building a <see cref="ScriptExecutionContext"/> in executor tests~ ✨.
/// </summary>
internal static class ScriptTestHarness
{
    public static (ScriptExecutionContext Context, IWorkflowScriptApi Api) BuildContext(
        IReadOnlyDictionary<string, object?>? inputs = null,
        IReadOnlyDictionary<string, object?>? variables = null,
        ScriptExecutionConfig? config = null,
        IReadOnlyList<ScriptLibrarySource>? libraries = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        var cfg = config ?? ScriptExecutionConfig.Default;
        var api = new WorkflowScriptApi(new WorkflowScriptApiOptions
        {
            Variables = variables ?? new Dictionary<string, object?>(),
            Config = cfg,
            ExecutionId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            NodeId = "test-node",
            Logger = NullLogger.Instance,
            HttpClientFactory = httpClientFactory,
        });

        var context = new ScriptExecutionContext
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Variables = variables ?? new Dictionary<string, object?>(),
            Api = api,
            Config = cfg,
            ExecutionId = Guid.NewGuid(),
            WorkflowId = Guid.NewGuid(),
            NodeId = "test-node",
            Logger = NullLogger.Instance,
            Libraries = libraries ?? Array.Empty<ScriptLibrarySource>(),
        };

        return (context, api);
    }
}
