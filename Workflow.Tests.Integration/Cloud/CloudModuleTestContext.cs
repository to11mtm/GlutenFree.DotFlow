// <copyright file="CloudModuleTestContext.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Integration.Cloud;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Cloud;

/// <summary>
/// 🧰 Shared DI + context helper for cloud-module integration tests~ ☁️✨.
/// </summary>
internal static class CloudModuleTestContext
{
    public static ServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.AddWorkflowModules();      // IWorkflowPathValidator for localPath checks
        sc.AddCloudStorageModules();  // registry + client factory
        return sc.BuildServiceProvider();
    }

    public static ModuleExecutionContext Context(IServiceProvider services, Dictionary<string, object?> props)
        => new()
        {
            Inputs = new Dictionary<string, object?>(),
            Properties = props,
            Variables = new Dictionary<string, object?>(),
            Logger = NullLogger.Instance,
            Services = services,
            ExecutionId = Guid.NewGuid(),
            NodeId = "cloud-it-node",
        };
}
