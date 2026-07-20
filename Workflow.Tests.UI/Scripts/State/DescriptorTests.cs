// <copyright file="DescriptorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts;

using System.Linq;
using System.Reflection;
using FluentAssertions;
using Workflow.Scripting.Abstractions;
using Workflow.UI.Client.Scripts.State;
using Xunit;

/// <summary>
/// 💡 Phase 3.4.1 (D4) — Guards the hand-authored <see cref="WorkflowApiDescriptor"/> against drift
/// from the real <see cref="IWorkflowScriptApi"/> contract, plus descriptor sanity checks~ ✨.
/// </summary>
public sealed class DescriptorTests
{
    [Fact]
    public void Descriptor_CoversWorkflowApi_NoDrift()
    {
        var contractMethods = typeof(IWorkflowScriptApi)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => !WorkflowApiDescriptor.ExecutorOnlyMembers.Contains(n))
            .ToHashSet();

        var describedMethods = WorkflowApiDescriptor.Methods.Select(m => m.ClrName).ToHashSet();

        describedMethods.Should().BeEquivalentTo(
            contractMethods,
            "the descriptor must cover exactly the user-facing IWorkflowScriptApi surface — update WorkflowApiDescriptor when the interface changes");
    }

    [Fact]
    public void Descriptor_MethodsHaveJsName_Summary_Category()
    {
        foreach (var m in WorkflowApiDescriptor.Methods)
        {
            m.JsName.Should().NotBeNullOrWhiteSpace();
            m.JsName[0].Should().Be(char.ToLowerInvariant(m.JsName[0]), "JS names are camelCase");
            m.Summary.Should().NotBeNullOrWhiteSpace();
            WorkflowApiDescriptor.Categories.Should().Contain(m.Category);
        }
    }

    [Fact]
    public void Descriptor_GatedMethods_FlaggedHttpAndFile()
    {
        var gated = WorkflowApiDescriptor.Methods.Where(m => m.Gated).Select(m => m.JsName).ToList();
        gated.Should().Contain(new[] { "httpGet", "httpPost", "httpPut", "httpDelete", "readFile", "writeFile", "fileExists", "deleteFile" });

        WorkflowApiDescriptor.Methods.Where(m => m.Category is "HTTP" or "Files").Should().OnlyContain(m => m.Gated);
        WorkflowApiDescriptor.Methods.Where(m => m.Category is "Variables" or "Logging").Should().OnlyContain(m => !m.Gated);
    }

    [Fact]
    public void Descriptor_CallSnippet_IsWorkflowPrefixed()
    {
        var get = WorkflowApiDescriptor.Methods.Single(m => m.ClrName == "GetVariable");
        get.CallSnippet.Should().Be("workflow.getVariable(name)");
        get.TypedSignature.Should().Be("getVariable(name: string): object?");
    }

    [Fact]
    public void Descriptor_Search_Filters()
    {
        WorkflowApiDescriptor.Search("http").Should().OnlyContain(m => m.Category == "HTTP");
        WorkflowApiDescriptor.Search(null).Should().HaveCount(WorkflowApiDescriptor.Methods.Count);
        WorkflowApiDescriptor.Search("variable").Should().Contain(m => m.JsName == "getVariable");
    }
}
