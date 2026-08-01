// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;

namespace Workflow.Core.Models;

/// <summary>
/// Defines a workflow variable that can be accessed by nodes during execution. 💾.
/// </summary>
/// <param name="Name">Unique name of the variable. 🏷️.</param>
/// <param name="Type">Data type of the variable. 🎨.</param>
/// <param name="InitialValue">Initial value for the variable (JSON element). Can be null. ✨.</param>
/// <param name="Description">Description of what this variable is used for. 📝.</param>
/// <param name="Seed">
/// Whether <paramref name="InitialValue"/> overrides a value persisted by a previous run, or only
/// seeds the variable when nothing is stored yet. Defaults to
/// <see cref="VariableSeedMode.SeedOnly"/>. 🌱.
/// </param>
/// <param name="IsSecret">
/// Whether this variable holds a credential. Secret variables are masked in the designer and
/// redacted from persisted execution records. A secret variable must not carry an
/// <paramref name="InitialValue"/> — workflow definitions are exported and version-controlled, so
/// they must never contain a credential. 🔒.
/// </param>
/// <remarks>
/// CopilotNote: Variables provide shared state across the workflow!
/// Nodes can read and write variables using GetVariable and SetVariable modules. Super useful! 💖.
/// <para>
/// Phase 3.5 (V1.2) added <paramref name="Seed"/> and <paramref name="IsSecret"/> as trailing
/// optional parameters, so definitions serialised before they existed deserialise unchanged~ ✨.
/// </para>
/// </remarks>
public record VariableDefinition(
    string Name,
    PropertyType Type,
    JsonElement? InitialValue = null,
    string? Description = null,
    VariableSeedMode Seed = VariableSeedMode.SeedOnly,
    bool IsSecret = false);
