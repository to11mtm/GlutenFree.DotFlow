// <copyright file="ScriptStudioHandoff.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Scripts.State;

using System;

/// <summary>
/// 🔗 Phase 3.4.5 (D9) — A scoped carrier for the designer ↔ Script Studio round-trip. The designer
/// stages a **request** (a node's code+language + a return URL) and navigates to <c>/scripts</c>;
/// Script Studio seeds itself from it and, on "Apply to node", stages a **result** and navigates
/// back; the designer consumes the result and applies it via an undoable edit. Framework-free (D2)
/// so it survives page navigation as an app-scoped service and ports cleanly~ ✨.
/// </summary>
public sealed class ScriptStudioHandoff
{
    /// <summary>Gets a value indicating whether an edit request is pending for Script Studio.</summary>
    public bool HasRequest { get; private set; }

    /// <summary>Gets the node id the request/result targets.</summary>
    public string? NodeId { get; private set; }

    /// <summary>Gets the seeded code (request) — Script Studio opens with this.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Gets the seeded language (request).</summary>
    public string Language { get; private set; } = "javascript";

    /// <summary>Gets the URL to return to after applying (usually the designer).</summary>
    public string? ReturnUrl { get; private set; }

    /// <summary>Gets a value indicating whether an edited result is waiting for the designer.</summary>
    public bool HasResult { get; private set; }

    /// <summary>Gets the edited code (result).</summary>
    public string? ResultCode { get; private set; }

    /// <summary>Raised whenever the handoff changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Stages an edit request (designer → studio)~ ➡️.</summary>
    /// <param name="nodeId">The node being edited.</param>
    /// <param name="code">The node's current code.</param>
    /// <param name="language">The node's language.</param>
    /// <param name="returnUrl">Where to navigate after applying.</param>
    public void Request(string nodeId, string code, string language, string? returnUrl)
    {
        this.NodeId = nodeId;
        this.Code = code ?? string.Empty;
        this.Language = string.IsNullOrWhiteSpace(language) ? "javascript" : language;
        this.ReturnUrl = returnUrl;
        this.HasRequest = true;
        this.HasResult = false;
        this.ResultCode = null;
        this.Changed?.Invoke();
    }

    /// <summary>Consumes the pending request (studio took it)~ 📥.</summary>
    /// <returns>The request tuple, or null when none is pending.</returns>
    public (string NodeId, string Code, string Language, string? ReturnUrl)? TakeRequest()
    {
        if (!this.HasRequest || this.NodeId is null)
        {
            return null;
        }

        this.HasRequest = false;
        var result = (this.NodeId, this.Code, this.Language, this.ReturnUrl);
        this.Changed?.Invoke();
        return result;
    }

    /// <summary>Stages an edited result (studio → designer)~ ⬅️.</summary>
    /// <param name="nodeId">The node the result applies to.</param>
    /// <param name="code">The edited code.</param>
    public void Fulfill(string nodeId, string code)
    {
        this.NodeId = nodeId;
        this.ResultCode = code ?? string.Empty;
        this.HasResult = true;
        this.HasRequest = false;
        this.Changed?.Invoke();
    }

    /// <summary>Consumes the pending result (designer applied it)~ 📤.</summary>
    /// <returns>The (nodeId, code) result, or null when none is pending.</returns>
    public (string NodeId, string Code)? TakeResult()
    {
        if (!this.HasResult || this.NodeId is null || this.ResultCode is null)
        {
            return null;
        }

        var result = (this.NodeId, this.ResultCode);
        this.HasResult = false;
        this.ResultCode = null;
        this.Changed?.Invoke();
        return result;
    }
}
