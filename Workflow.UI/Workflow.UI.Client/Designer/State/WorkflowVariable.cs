// <copyright file="WorkflowVariable.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System.Text.Json;

/// <summary>
/// 💾 A workflow variable declaration as the designer models it — the client's view of
/// <c>Workflow.Core.Models.VariableDefinition</c>. Framework-free (D2), so the UI keeps its own
/// shape rather than referencing the engine's~ ✨.
/// </summary>
/// <param name="Name">The variable name (also the map key in the document).</param>
/// <param name="Type">The declared value type.</param>
/// <param name="InitialValue">
/// The declared initial value, or null when none is declared. A JSON <c>null</c> element is
/// distinct from null here: it means "declared, with the value null".
/// </param>
/// <param name="Description">Optional documentation shown in tooltips and the token picker.</param>
/// <param name="Seed">Whether the initial value overrides a value persisted by a previous run.</param>
/// <param name="IsSecret">Whether the variable holds a credential (masked in the UI).</param>
public sealed record WorkflowVariable(
    string Name,
    VariableValueType Type,
    JsonElement? InitialValue,
    string? Description,
    VariableSeed Seed,
    bool IsSecret);

/// <summary>
/// 🎨 Mirrors <c>Workflow.Core.Models.PropertyType</c>. Values are pinned explicitly because the
/// wire format carries them as <em>numbers</em> — no <c>JsonStringEnumConverter</c> is registered
/// anywhere in the solution, so the ordinals are the contract.
/// </summary>
/// <remarks>
/// A drift-guard test in <c>Workflow.Tests.UI</c> asserts this stays aligned with the Core enum, so
/// reordering <c>PropertyType</c> fails a test rather than silently mis-typing every variable.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Member names mirror Workflow.Core.Models.PropertyType exactly, by design.")]
public enum VariableValueType
{
    /// <summary>A text string value.</summary>
    String = 0,

    /// <summary>A 32-bit integer value.</summary>
    Int = 1,

    /// <summary>A 64-bit long integer value.</summary>
    Long = 2,

    /// <summary>A decimal number value.</summary>
    Decimal = 3,

    /// <summary>A boolean true/false value.</summary>
    Boolean = 4,

    /// <summary>A date and time value.</summary>
    DateTime = 5,

    /// <summary>A time span/duration value.</summary>
    TimeSpan = 6,

    /// <summary>A globally unique identifier.</summary>
    Guid = 7,

    /// <summary>A complex object (JSON).</summary>
    Object = 8,

    /// <summary>An array/collection of values.</summary>
    Array = 9,

    /// <summary>A reference to another node's output port.</summary>
    Connection = 10,

    /// <summary>A reference to a workflow variable.</summary>
    Variable = 11,
}

/// <summary>
/// 🌱 Mirrors <c>Workflow.Core.Models.VariableSeedMode</c>. Also carried as a number on the wire.
/// </summary>
public enum VariableSeed
{
    /// <summary>The initial value only applies when nothing is stored for the workflow yet.</summary>
    SeedOnly = 0,

    /// <summary>The initial value always applies, overwriting anything a previous run persisted.</summary>
    AlwaysOverride = 1,
}
