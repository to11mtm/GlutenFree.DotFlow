// <copyright file="LinqStudioHandoff.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Linq.State;

using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// 🔗 Linq Studio — the designer ↔ studio round-trip carrier (same D9 pattern as
/// <c>ScriptStudioHandoff</c>). The designer stages a linq node's authoring state and navigates
/// to <c>/linq-studio</c>; the studio seeds from it and, on Publish/Apply, stages a result the
/// designer applies as one undoable property edit. Framework-free (D2)~ ✨.
/// </summary>
public sealed class LinqStudioHandoff
{
    /// <summary>Gets a value indicating whether an edit request is pending for the studio.</summary>
    public bool HasRequest { get; private set; }

    /// <summary>Gets the node id the request/result targets.</summary>
    public string? NodeId { get; private set; }

    /// <summary>Gets the definition id (for compile cache keying).</summary>
    public string? DefinitionId { get; private set; }

    /// <summary>Gets the seeded user code.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Gets the seeded connection id.</summary>
    public string ConnectionId { get; private set; } = string.Empty;

    /// <summary>Gets the seeded table names.</summary>
    public IReadOnlyList<string> TableNames { get; private set; } = Array.Empty<string>();

    /// <summary>Gets the URL to return to after applying (usually the designer).</summary>
    public string? ReturnUrl { get; private set; }

    /// <summary>Gets a value indicating whether a published result awaits the designer.</summary>
    public bool HasResult { get; private set; }

    /// <summary>Gets the result property bag (userCode/connectionId/tableNames/compiledAssemblyKey).</summary>
    public IReadOnlyDictionary<string, JsonElement>? ResultProperties { get; private set; }

    /// <summary>Raised whenever the handoff changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Stages an edit request (designer → studio)~ ➡️.</summary>
    /// <param name="nodeId">The node being edited.</param>
    /// <param name="definitionId">The owning workflow definition id.</param>
    /// <param name="code">The node's current user code.</param>
    /// <param name="connectionId">The node's connection id.</param>
    /// <param name="tableNames">The node's selected table names.</param>
    /// <param name="returnUrl">Where to navigate after applying.</param>
    public void Request(
        string nodeId,
        string? definitionId,
        string code,
        string connectionId,
        IReadOnlyList<string> tableNames,
        string? returnUrl)
    {
        this.NodeId = nodeId;
        this.DefinitionId = definitionId;
        this.Code = code ?? string.Empty;
        this.ConnectionId = connectionId ?? string.Empty;
        this.TableNames = tableNames ?? Array.Empty<string>();
        this.ReturnUrl = returnUrl;
        this.HasRequest = true;
        this.HasResult = false;
        this.ResultProperties = null;
        this.Changed?.Invoke();
    }

    /// <summary>Consumes the pending request (the studio took it)~ 📥.</summary>
    /// <returns>The request, or null when none is pending.</returns>
    public (string NodeId, string? DefinitionId, string Code, string ConnectionId, IReadOnlyList<string> TableNames, string? ReturnUrl)? TakeRequest()
    {
        if (!this.HasRequest || this.NodeId is null)
        {
            return null;
        }

        this.HasRequest = false;
        var result = (this.NodeId, this.DefinitionId, this.Code, this.ConnectionId, this.TableNames, this.ReturnUrl);
        this.Changed?.Invoke();
        return result;
    }

    /// <summary>Stages a published result (studio → designer)~ ⬅️.</summary>
    /// <param name="nodeId">The node the result applies to.</param>
    /// <param name="properties">The property values to write (userCode/connectionId/tableNames/compiledAssemblyKey).</param>
    public void Fulfill(string nodeId, IReadOnlyDictionary<string, JsonElement> properties)
    {
        this.NodeId = nodeId;
        this.ResultProperties = properties;
        this.HasResult = true;
        this.HasRequest = false;
        this.Changed?.Invoke();
    }

    /// <summary>Consumes the pending result (the designer applied it)~ 📤.</summary>
    /// <returns>The (nodeId, properties) result, or null when none is pending.</returns>
    public (string NodeId, IReadOnlyDictionary<string, JsonElement> Properties)? TakeResult()
    {
        if (!this.HasResult || this.NodeId is null || this.ResultProperties is null)
        {
            return null;
        }

        var result = (this.NodeId, this.ResultProperties);
        this.HasResult = false;
        this.ResultProperties = null;
        this.Changed?.Invoke();
        return result;
    }
}
