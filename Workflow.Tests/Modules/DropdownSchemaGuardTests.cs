// <copyright file="DropdownSchemaGuardTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Xunit;

/// <summary>
/// 🧪 UX-feedback F1 guard — every builtin property that declares a Dropdown editor MUST also
/// declare its allowed values (and a sane default), otherwise the designer renders an empty
/// select. Covers core builtins plus the Cloud and Database module packs~ ✨.
/// </summary>
public sealed class DropdownSchemaGuardTests
{
    private static IEnumerable<IWorkflowModule> AllModules()
    {
        foreach (var m in BuiltinModules.GetAll())
        {
            yield return m;
        }

        yield return new Workflow.Modules.Cloud.Builtin.S3Module();
        yield return new Workflow.Modules.Cloud.Builtin.AzureBlobModule();
        yield return new Workflow.Modules.Database.Builtin.DatabaseQueryModule();
        yield return new Workflow.Modules.Database.Builtin.DatabaseExecuteModule();
        yield return new Workflow.Modules.Database.Builtin.DatabaseTransactionModule();
        yield return new Workflow.Modules.Database.Builtin.DatabaseBulkInsertModule();
    }

    public static TheoryData<string, string> DropdownProperties()
    {
        var data = new TheoryData<string, string>();
        foreach (var module in AllModules())
        {
            foreach (var prop in module.Schema.Properties.Where(p => p.EditorType == PropertyEditorType.Dropdown))
            {
                data.Add(module.ModuleId, prop.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DropdownProperties))]
    public void Dropdown_DeclaresAllowedValues(string moduleId, string propertyName)
    {
        var module = AllModules().Single(m => m.ModuleId == moduleId);
        var prop = module.Schema.Properties.Single(p => p.Name == propertyName);

        prop.AllowedValues.Should().NotBeNull(
            because: $"{moduleId}.{propertyName} is a Dropdown — without AllowedValues the designer renders an empty select~ 💔");
        prop.AllowedValues!.Value.Count.Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(DropdownProperties))]
    public void Dropdown_DefaultValue_IsAllowed(string moduleId, string propertyName)
    {
        var module = AllModules().Single(m => m.ModuleId == moduleId);
        var prop = module.Schema.Properties.Single(p => p.Name == propertyName);

        if (prop.DefaultValue is null)
        {
            // Optional dropdowns may default to unset — the designer shows an "(auto)" option.
            prop.IsRequired.Should().BeFalse(
                because: $"{moduleId}.{propertyName} has no default, so it must be optional~ 💔");
            return;
        }

        prop.AllowedValues!.Value.AsEnumerable().Should().Contain(
            prop.DefaultValue,
            because: $"{moduleId}.{propertyName}'s default must be one of its allowed values~ 💔");
    }

    [Fact]
    public void GuardCoversAtLeastTheKnownDropdowns()
    {
        // Sanity: the reflection sweep actually finds the dropdowns we fixed (guards the guard).
        var found = DropdownProperties().Select(row => $"{row[0]}.{row[1]}").ToList();
        found.Should().Contain(new[]
        {
            "builtin.transform.join.joinType",
            "builtin.transform.string.operation",
            "builtin.http.request.method",
            "builtin.fanin.mode",
            "builtin.log.level",
        });
    }
}
