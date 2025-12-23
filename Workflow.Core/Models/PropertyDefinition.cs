// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;

namespace Workflow.Core.Models;

/// <summary>
/// Defines a property for a module (input, output, or configuration). 📋
/// </summary>
/// <param name="Name">The unique name of the property. 🏷️</param>
/// <param name="Type">The data type of the property. 🎨</param>
/// <param name="Description">A human-readable description of what this property does. 📝</param>
/// <param name="IsRequired">Whether this property must be provided. Default is false. ✅</param>
/// <param name="DefaultValue">The default value if not provided (JSON element). Can be null. 💫</param>
/// <param name="ValidationRules">Collection of validation rules for this property. 🛡️</param>
/// <param name="DisplayMetadata">Additional metadata for UI display (hints, grouping, etc.). 🎀</param>
/// <remarks>
/// CopilotNote: This defines the schema for a property. The actual value is stored elsewhere!
/// DefaultValue is stored as JsonElement for flexibility - it can be any JSON type! 💖
/// </remarks>
public record PropertyDefinition(
	string Name,
	PropertyType Type,
	string Description,
	bool IsRequired = false,
	JsonElement? DefaultValue = null,
	IReadOnlyList<ValidationRule>? ValidationRules = null,
	IReadOnlyDictionary<string, string>? DisplayMetadata = null);

/// <summary>
/// Defines a validation rule for a property value. ✨
/// </summary>
/// <param name="RuleType">The type of validation to perform. 🎯</param>
/// <param name="Parameters">Parameters for the validation rule (e.g., min/max values). 📊</param>
/// <param name="ErrorMessage">Custom error message if validation fails. 💬</param>
public record ValidationRule(
	ValidationRuleType RuleType,
	IReadOnlyDictionary<string, object>? Parameters = null,
	string? ErrorMessage = null);

/// <summary>
/// Types of validation rules that can be applied to properties. 🔍
/// </summary>
public enum ValidationRuleType
{
	/// <summary>
	/// Minimum length for strings. 📏
	/// </summary>
	MinLength,

	/// <summary>
	/// Maximum length for strings. 📐
	/// </summary>
	MaxLength,

	/// <summary>
	/// Minimum value for numbers. ⬇️
	/// </summary>
	Min,

	/// <summary>
	/// Maximum value for numbers. ⬆️
	/// </summary>
	Max,

	/// <summary>
	/// Regular expression pattern matching for strings. 🔤
	/// </summary>
	Regex,

	/// <summary>
	/// Must be one of the allowed enum values. 🎭
	/// </summary>
	Enum,

	/// <summary>
	/// Custom validation expression. 💫
	/// </summary>
	Custom,
}

