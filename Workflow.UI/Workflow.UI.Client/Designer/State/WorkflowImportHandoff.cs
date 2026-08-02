// <copyright file="WorkflowImportHandoff.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Designer.State;

using System;

/// <summary>
/// 🔗 Phase 3.6 (E2) — carries an imported workflow from the list page into the designer.
/// </summary>
/// <remarks>
/// Same one-shot pattern as <c>LinqStudioHandoff</c> and <c>ScriptStudioHandoff</c>: the list page
/// stages the parsed workflow and navigates to <c>/designer/imported</c>; the designer takes it and
/// opens it **unsaved**, so nothing is written until the user commits. Framework-free (D2)~ ✨.
/// </remarks>
public sealed class WorkflowImportHandoff
{
    /// <summary>The route the designer serves imported workflows on~ 🛣️.</summary>
    public const string RouteId = "imported";

    /// <summary>Gets a value indicating whether an imported workflow is waiting~ 📦.</summary>
    public bool HasPending { get; private set; }

    /// <summary>Gets the staged request.</summary>
    public WorkflowImportRequest? Request { get; private set; }

    /// <summary>Raised whenever the handoff changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Stages an imported workflow for the designer~ ➡️.</summary>
    /// <param name="request">The workflow and its overwrite intent.</param>
    public void Stage(WorkflowImportRequest request)
    {
        this.Request = request;
        this.HasPending = true;
        this.Changed?.Invoke();
    }

    /// <summary>Consumes the staged workflow~ 📥.</summary>
    /// <returns>The request, or null when nothing is pending.</returns>
    public WorkflowImportRequest? Take()
    {
        if (!this.HasPending || this.Request is null)
        {
            return null;
        }

        var request = this.Request;
        this.HasPending = false;
        this.Request = null;
        this.Changed?.Invoke();
        return request;
    }
}
