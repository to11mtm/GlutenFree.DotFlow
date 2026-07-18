// <copyright file="TransformModuleTestBase.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Transform;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Core.Abstractions;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧰 Shared fixture for transform-module tests — DI wired with the modules + 2.2.5 evaluators~ 🔄✨.
/// </summary>
public abstract class TransformModuleTestBase : IDisposable
{
    protected TransformModuleTestBase()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        sc.AddLogging();
        sc.AddSingleton<IExpressionEvaluator, JintExpressionEvaluator>();
        sc.AddKeyedSingleton<IExpressionEvaluator, DynamicExpressoEvaluator>("csharp");
        this.Services = sc.BuildServiceProvider();
    }

    protected ServiceProvider Services { get; }

    public void Dispose()
    {
        this.Services.Dispose();
        GC.SuppressFinalize(this);
    }

    protected ModuleExecutionContext Context(
        Dictionary<string, object?> properties,
        Dictionary<string, object?>? inputs = null,
        Dictionary<string, object?>? variables = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties,
            Variables = variables ?? new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = this.Services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "transform-node-1",
        };

    protected static Dictionary<string, object?> Rec(params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
        {
            d[k] = v;
        }

        return d;
    }
}
