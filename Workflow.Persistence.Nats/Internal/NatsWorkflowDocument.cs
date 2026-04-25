// <copyright file="NatsWorkflowDocument.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Nats.Internal;

using Workflow.Core.Models;

/// <summary>
/// 📦 Internal document wrapper stored in the <c>WF_WORKFLOWS</c> KV bucket~ ✨
/// </summary>
/// <remarks>
/// CopilotNote: NATS KV is a KV store not a SQL DB, so soft-delete semantics
/// are implemented by storing <c>IsActive = false</c> inside the document.
/// Hard-delete (Purge) uses NATS KV <c>PurgeAsync</c> which removes all revisions~ 🗑️
/// </remarks>
internal record NatsWorkflowDocument(
    WorkflowDefinition Definition,
    bool IsActive,
    string CreatedAt,
    string UpdatedAt);

