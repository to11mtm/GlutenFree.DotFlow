// <copyright file="PaletteDragState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Services;

using System;

/// <summary>
/// 📦 Phase 3.3.b.0 — Carries the module id being dragged from the palette to the canvas. A tiny
/// shared service is used instead of HTML5 <c>dataTransfer</c> because it's simpler to marshal and
/// testable without a real browser~ ✨.
/// </summary>
public sealed class PaletteDragState
{
    /// <summary>Raised when a drag begins or ends~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets the module id currently being dragged (null when not dragging).</summary>
    public string? DraggingModuleId { get; private set; }

    /// <summary>Begins a drag for the given module~ 🫳.</summary>
    /// <param name="moduleId">The module id.</param>
    public void Begin(string moduleId)
    {
        this.DraggingModuleId = moduleId;
        this.Changed?.Invoke();
    }

    /// <summary>Ends the drag and returns the module id that was dragged (or null)~ 🫴.</summary>
    /// <returns>The dragged module id, or null.</returns>
    public string? End()
    {
        var id = this.DraggingModuleId;
        this.DraggingModuleId = null;
        if (id is not null)
        {
            this.Changed?.Invoke();
        }

        return id;
    }
}
