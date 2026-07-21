// <copyright file="WorkflowModuleAttribute.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Discovery;

using System;

/// <summary>
/// 🏷️ Optional attribute for workflow module classes to provide metadata overrides
/// and control auto-discovery behavior. When applied, it can override the module's
/// ID, category, and description, or exclude it from discovery entirely~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This attribute is OPTIONAL! The discovery service will find any
/// public non-abstract class implementing <c>IWorkflowModule</c> regardless.
/// Use this attribute to:
/// - Override metadata without changing the class itself
/// - Exclude a module from auto-discovery with <c>Ignore = true</c>
/// - Add a description override for the module palette 💖.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [WorkflowModule(Category = "Custom", Description = "My awesome module!")]
/// public class MyModule : IWorkflowModule { ... }
///
/// [WorkflowModule(Ignore = true)] // 🚫 Skip this one during scanning!
/// public class ExperimentalModule : IWorkflowModule { ... }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorkflowModuleAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an optional module ID override. When set, replaces the
    /// module's <c>ModuleId</c> property during discovery registration. 🆔.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Use sparingly — the module class should own its own ID.
    /// This is mainly for aliasing or renaming scenarios~ 💫.
    /// </remarks>
    public string? ModuleId { get; set; }

    /// <summary>
    /// Gets or sets an optional category override. When set, replaces the
    /// module's <c>Category</c> property during discovery registration. 📁.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets an optional description override. When set, replaces the
    /// module's <c>Description</c> property during discovery registration. 📝.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this module should be excluded
    /// from auto-discovery. Defaults to <c>false</c>. 🚫.
    /// </summary>
    /// <remarks>
    /// CopilotNote: Set to <c>true</c> to prevent the module discovery service
    /// from picking up this class. Useful for base classes, experimental modules,
    /// or test-only implementations that shouldn't appear in production~ 🧪.
    /// </remarks>
    public bool Ignore { get; set; }
}
