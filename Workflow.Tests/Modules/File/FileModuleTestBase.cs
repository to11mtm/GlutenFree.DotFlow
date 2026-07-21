// <copyright file="FileModuleTestBase.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.FileSystem;

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules;
using Workflow.Modules.Abstractions;

/// <summary>
/// 🧰 Shared fixture for file-module tests — a temp sandbox dir + a DI container wired with
/// <c>AddFileSystemModules()</c>~ 📁✨.
/// </summary>
public abstract class FileModuleTestBase : IDisposable
{
    protected FileModuleTestBase()
    {
        this.TempDir = Path.Combine(Path.GetTempPath(), "dotflow-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.TempDir);

        var sc = new ServiceCollection();
        sc.AddWorkflowModules();
        this.Services = sc.BuildServiceProvider();
    }

    protected string TempDir { get; }

    protected ServiceProvider Services { get; }

    public void Dispose()
    {
        this.Services.Dispose();
        try
        {
            if (Directory.Exists(this.TempDir))
            {
                Directory.Delete(this.TempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup~ 🧹
        }

        GC.SuppressFinalize(this);
    }

    protected string PathIn(string name) => Path.Combine(this.TempDir, name);

    protected ModuleExecutionContext Context(
        Dictionary<string, object?> properties,
        Dictionary<string, object?>? inputs = null)
        => new()
        {
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Properties = properties,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = this.Services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "file-node-1",
        };
}
