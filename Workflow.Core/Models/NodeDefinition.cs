// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using LanguageExt;

namespace Workflow.Core.Models;

/// <summary>
/// Defines a single node in a workflow. 🧩
/// </summary>
/// <param name="Id">Unique identifier for this node within the workflow. Must be unique! 🆔</param>
/// <param name="ModuleId">The ID of the module type this node uses. 📦</param>
/// <param name="Name">Display name for this node (shown in UI). 🏷️</param>
/// <param name="Properties">Immutable map of configuration properties for this node instance (JSON elements). ⚙️</param>
/// <param name="Position">UI position for the visual designer. 🎨</param>
/// <param name="ErrorHandling">Error handling configuration (overrides workflow default). 🛡️</param>
/// <param name="Timeout">Maximum execution time in milliseconds. Null means no timeout. ⏱️</param>
/// <param name="RetryPolicy">Retry behavior on errors. 🔄</param>
/// <param name="Metadata">Immutable map of additional metadata for extensibility. 💫</param>
/// <remarks>
/// CopilotNote: Nodes are the "vertices" in our workflow graph!
/// Properties are stored as JsonElements for maximum flexibility - they can be any JSON type!
/// Uses LanguageExt HashMap for structural equality and immutability! 💖
/// Each node is an instance of a module, configured with specific property values, nya~! ✨
/// </remarks>
public record NodeDefinition(
    string Id,
    string ModuleId,
    string Name,
    HashMap<string, JsonElement> Properties,
    Position? Position = null,
    ErrorHandling? ErrorHandling = null,
    int? Timeout = null,
    RetryPolicy? RetryPolicy = null,
    HashMap<string, string>? Metadata = null);

