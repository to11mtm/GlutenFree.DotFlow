// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Diagnostics.CodeAnalysis;

namespace Workflow.Core.Models;

/// <summary>
/// Defines the data types supported for module properties, inputs, and outputs. 🎀.
/// </summary>
/// <remarks>
/// CopilotNote: This enum is used throughout the module system to define property schemas
/// and validate property values. The Connection and Variable types are special references! 💫.
/// </remarks>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Need for description")]
public enum PropertyType
{
    /// <summary>
    /// A text string value. 📝.
    /// </summary>
    String,

    /// <summary>
    /// A 32-bit integer value. 🔢.
    /// </summary>
    Int,

    /// <summary>
    /// A 64-bit long integer value. 📊.
    /// </summary>
    Long,

    /// <summary>
    /// A decimal number value (for precise calculations). 💰.
    /// </summary>
    Decimal,

    /// <summary>
    /// A boolean true/false value. ✅.
    /// </summary>
    Boolean,

    /// <summary>
    /// A date and time value. ⏰.
    /// </summary>
    DateTime,

    /// <summary>
    /// A time span/duration value. ⌛.
    /// </summary>
    TimeSpan,

    /// <summary>
    /// A globally unique identifier (GUID). 🆔.
    /// </summary>
    Guid,

    /// <summary>
    /// A complex object (JSON). 📦.
    /// </summary>
    Object,

    /// <summary>
    /// An array/collection of values. 📚.
    /// </summary>
    Array,

    /// <summary>
    /// A reference to another node's output port. 🔗.
    /// </summary>
    Connection,

    /// <summary>
    /// A reference to a workflow variable. 💾.
    /// </summary>
    Variable,
}
