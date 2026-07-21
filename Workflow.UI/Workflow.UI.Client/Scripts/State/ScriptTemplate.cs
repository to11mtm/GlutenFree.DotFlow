// <copyright file="ScriptTemplate.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

/// <summary>
/// 📄 Phase 3.4.2 (D7) — A single insertable starter snippet. Framework-free client data (D2)~ ✨.
/// </summary>
/// <param name="Id">A stable id.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">A one-line description.</param>
/// <param name="Language">The language id it targets (e.g. <c>javascript</c>).</param>
/// <param name="Category">The grouping (HTTP/Data/Variables/Logging/Files/Utilities/Error handling).</param>
/// <param name="Code">The snippet body.</param>
public sealed record ScriptTemplate(
    string Id,
    string Name,
    string Description,
    string Language,
    string Category,
    string Code);
