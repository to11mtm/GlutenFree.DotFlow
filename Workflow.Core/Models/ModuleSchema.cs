// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

using System;
using LanguageExt;

/// <summary>
/// Defines the schema for a workflow module (its inputs, outputs, and configuration). 📐
/// </summary>
/// <param name="Inputs">Immutable array of input port definitions. 📥</param>
/// <param name="Outputs">Immutable array of output port definitions. 📤</param>
/// <param name="Properties">Immutable array of configuration property definitions. ⚙️</param>
/// <remarks>
/// <para>
/// CopilotNote: This is the "contract" that defines what a module can do!
/// Modules expose this schema so the workflow designer knows what ports and properties to show.
/// Uses LanguageExt Arr for structural equality! Super smart! 💖
/// </para>
/// </remarks>
public record ModuleSchema(
    Arr<PortDefinition> Inputs,
    Arr<PortDefinition> Outputs,
    Arr<ModulePropertyDefinition> Properties)
{
    /// <summary>
    /// Creates an empty schema with no inputs, outputs, or properties.
    /// </summary>
    public static ModuleSchema Empty => new(
        Arr<PortDefinition>.Empty,
        Arr<PortDefinition>.Empty,
        Arr<ModulePropertyDefinition>.Empty);
}

/// <summary>
/// 🔌 Defines an input or output port for a module.
/// Ports are the connection points for data flow between nodes.
/// </summary>
/// <param name="Name">The unique name of the port (used in connections). 🏷️</param>
/// <param name="DisplayName">The display name shown in UI. 🎨</param>
/// <param name="DataType">The .NET type of data this port handles. 📊</param>
/// <param name="Description">A human-readable description of what this port does. 📝</param>
/// <param name="IsRequired">Whether this port must be connected. Default is true for inputs. ✅</param>
/// <param name="DefaultValue">The default value if not connected (for optional inputs). 💫</param>
/// <remarks>
/// <para>
/// CopilotNote: Ports are different from properties! Ports carry data between nodes,
/// while properties are configuration settings for the node itself. Think of ports
/// as the plugs on the side of the node, and properties as the knobs on top~ 💖
/// </para>
/// </remarks>
public record PortDefinition(
    string Name,
    string DisplayName,
    Type DataType,
    string? Description = null,
    bool IsRequired = true,
    object? DefaultValue = null)
{
    /// <summary>
    /// Creates a port definition with name as display name.
    /// </summary>
    /// <param name="name">The port name (also used as display name).</param>
    /// <param name="dataType">The data type.</param>
    /// <param name="isRequired">Whether the port is required.</param>
    /// <returns>A new PortDefinition.</returns>
    public static PortDefinition Create(string name, Type dataType, bool isRequired = true)
        => new(name, name, dataType, null, isRequired, null);

    /// <summary>
    /// Creates a port definition for a generic type.
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="name">The port name.</param>
    /// <param name="isRequired">Whether the port is required.</param>
    /// <returns>A new PortDefinition.</returns>
    public static PortDefinition Create<T>(string name, bool isRequired = true)
        => new(name, name, typeof(T), null, isRequired, null);
}

/// <summary>
/// ⚙️ Defines a configurable property for a module.
/// Properties are settings that configure how the module behaves.
/// </summary>
/// <param name="Name">The unique name of the property. 🏷️</param>
/// <param name="DisplayName">The display name shown in UI. 🎨</param>
/// <param name="DataType">The .NET type of this property. 📊</param>
/// <param name="Description">A human-readable description of what this property does. 📝</param>
/// <param name="IsRequired">Whether this property must be provided. Default is false. ✅</param>
/// <param name="DefaultValue">The default value if not provided. 💫</param>
/// <param name="EditorType">The type of UI editor to use. 🖊️</param>
/// <param name="AllowedValues">Allowed values for dropdown/enum properties. 🎭</param>
/// <param name="ValidationRules">Immutable array of validation rules. 🛡️</param>
/// <param name="DisplayMetadata">Additional metadata for UI display. 🎀</param>
/// <remarks>
/// <para>
/// CopilotNote: Properties are different from ports! Properties configure the node's
/// behavior, while ports carry data between nodes. Properties are set at design time,
/// while port values flow at runtime~ 💖
/// </para>
/// </remarks>
public record ModulePropertyDefinition(
    string Name,
    string DisplayName,
    Type DataType,
    string? Description = null,
    bool IsRequired = false,
    object? DefaultValue = null,
    PropertyEditorType EditorType = PropertyEditorType.Text,
    Arr<object>? AllowedValues = null,
    Arr<ValidationRule>? ValidationRules = null,
    HashMap<string, string>? DisplayMetadata = null)
{
    /// <summary>
    /// Creates a property definition with name as display name.
    /// </summary>
    /// <param name="name">The property name (also used as display name).</param>
    /// <param name="dataType">The data type.</param>
    /// <param name="isRequired">Whether the property is required.</param>
    /// <returns>A new ModulePropertyDefinition.</returns>
    public static ModulePropertyDefinition Create(string name, Type dataType, bool isRequired = false)
        => new(name, name, dataType, null, isRequired);

    /// <summary>
    /// Creates a property definition for a generic type.
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="name">The property name.</param>
    /// <param name="isRequired">Whether the property is required.</param>
    /// <returns>A new ModulePropertyDefinition.</returns>
    public static ModulePropertyDefinition Create<T>(string name, bool isRequired = false)
        => new(name, name, typeof(T), null, isRequired);
}

/// <summary>
/// 🖊️ Types of property editors for the UI.
/// </summary>
public enum PropertyEditorType
{
    /// <summary>Single-line text input.</summary>
    Text,

    /// <summary>Multi-line text input.</summary>
    MultilineText,

    /// <summary>Numeric input.</summary>
    Number,

    /// <summary>Boolean checkbox.</summary>
    Boolean,

    /// <summary>Dropdown selection.</summary>
    Dropdown,

    /// <summary>File path picker.</summary>
    FilePath,

    /// <summary>Directory path picker.</summary>
    DirectoryPath,

    /// <summary>Connection string editor.</summary>
    ConnectionString,

    /// <summary>Expression editor.</summary>
    Expression,

    /// <summary>JSON editor.</summary>
    Json,

    /// <summary>Code editor.</summary>
    Code,
}

/// <summary>
/// Defines a validation rule for a property value. ✨
/// </summary>
/// <param name="RuleType">The type of validation to perform. 🎯</param>
/// <param name="Parameters">Immutable map of parameters for the validation rule. 📊</param>
/// <param name="ErrorMessage">Custom error message if validation fails. 💬</param>
public record ValidationRule(
    ValidationRuleType RuleType,
    HashMap<string, object>? Parameters = null,
    string? ErrorMessage = null);

/// <summary>
/// Types of validation rules that can be applied to properties. 🔍
/// </summary>
public enum ValidationRuleType
{
    /// <summary>Minimum length for strings. 📏</summary>
    MinLength,

    /// <summary>Maximum length for strings. 📐</summary>
    MaxLength,

    /// <summary>Minimum value for numbers. ⬇️</summary>
    Min,

    /// <summary>Maximum value for numbers. ⬆️</summary>
    Max,

    /// <summary>Regular expression pattern matching. 🔤</summary>
    Regex,

    /// <summary>Must be one of the allowed values. 🎭</summary>
    Enum,

    /// <summary>Custom validation expression. 💫</summary>
    Custom,
}

