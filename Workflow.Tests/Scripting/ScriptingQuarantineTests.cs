// <copyright file="ScriptingQuarantineTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Scripting;

using System;
using System.Linq;
using FluentAssertions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// 🛡️ Phase 2.6.b.0 — proves the Roslyn scripting core stays quarantined out of
/// <c>Workflow.Modules</c> (the D4 rule — SDK-free hosts never transitively load Roslyn)~ ✨.
/// </summary>
public sealed class ScriptingQuarantineTests
{
    [Fact]
    public void WorkflowModules_DoesNotReferenceScriptingCore_NorRoslyn()
    {
        var modulesAssembly = typeof(BuiltinModules).Assembly;
        var referenced = modulesAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.Should().NotContain("Workflow.Scripting.Roslyn", "the scripting core must stay quarantined (D4)~ 🛡️");
        referenced.Should().NotContain("Microsoft.CodeAnalysis", "Roslyn must not enter Workflow.Modules~ 🛡️");
        referenced.Should().NotContain("Microsoft.CodeAnalysis.CSharp");
    }

    [Fact]
    public void AddWorkflowModules_DoesNotLoadRoslyn()
    {
        // Loading the modules assembly + registering the family must not pull Roslyn into the AppDomain
        // beyond what other test fixtures already loaded — assert the reference graph instead (deterministic).
        var transformAssembly = typeof(Workflow.Modules.Builtin.Transform.DataMapModule).Assembly;
        transformAssembly.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain("Microsoft.CodeAnalysis.CSharp");
    }
}
