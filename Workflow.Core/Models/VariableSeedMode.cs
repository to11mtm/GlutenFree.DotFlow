// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

namespace Workflow.Core.Models;

/// <summary>
/// Controls how a declared variable's <see cref="VariableDefinition.InitialValue"/> interacts with
/// a value already persisted for the workflow. 🌱.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 3.5 (V1.2) — resolves plan question Q1. The execution's variable map is
/// layered lowest-to-highest as: global store → workflow store → declared initial value → run
/// inputs. This enum decides whether that third layer actually applies, so a workflow author can
/// choose between "seed me once, then remember what runs wrote" and "always start from this
/// value"~ 💖.
/// </remarks>
public enum VariableSeedMode
{
    /// <summary>
    /// The initial value only applies when nothing is stored for the workflow yet — a value
    /// persisted by a previous run wins. This is the default because it never silently discards
    /// state a run deliberately saved. 🌱.
    /// </summary>
    SeedOnly,

    /// <summary>
    /// The initial value always applies, overwriting anything a previous run persisted. Use for
    /// counters, flags, and anything that must start from a known state on every run. ♻️.
    /// </summary>
    AlwaysOverride,
}
