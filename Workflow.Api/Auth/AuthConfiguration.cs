// <copyright file="AuthConfiguration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using System;
using System.Collections.Generic;

/// <summary>
/// 🔐 Phase 2.7.7 — Scheme, policy, and role constants for the API auth surface~ ✨.
/// </summary>
public static class AuthConstants
{
    /// <summary>The API-key authentication scheme name~ 🔑.</summary>
    public const string ApiKeyScheme = "ApiKey";

    /// <summary>The header the API-key scheme reads the key from~ 🔑.</summary>
    public const string ApiKeyHeader = "X-API-Key";

    /// <summary>Policy: read workflow/execution resources~ 👀.</summary>
    public const string WorkflowReadPolicy = "WorkflowRead";

    /// <summary>Policy: create/update/delete workflows~ ✍️.</summary>
    public const string WorkflowWritePolicy = "WorkflowWrite";

    /// <summary>Policy: start/cancel executions~ ⚡.</summary>
    public const string WorkflowExecutePolicy = "WorkflowExecute";

    /// <summary>Policy: administrative operations~ 🛡️.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>The admin role~ 🛡️.</summary>
    public const string AdminRole = "Admin";

    /// <summary>The developer role (read/write/execute)~ 🧑‍💻.</summary>
    public const string DeveloperRole = "Developer";

    /// <summary>The viewer role (read-only)~ 👀.</summary>
    public const string ViewerRole = "Viewer";
}

/// <summary>
/// 🔐 Phase 2.7.7 — Bound configuration for API authentication (<c>Api:Auth</c>)~ ✨.
/// </summary>
public sealed class ApiAuthOptions
{
    /// <summary>The configuration section name~ 📇.</summary>
    public const string SectionName = "Api:Auth";

    /// <summary>Gets or sets a value indicating whether authentication is enforced globally.</summary>
    /// <remarks>Default <c>false</c> keeps dev/test hosts anonymous-friendly (Q1)~ 🌸.</remarks>
    public bool Require { get; set; }

    /// <summary>Gets the configured API keys (hashed at rest).</summary>
    public List<ApiKeyEntry> ApiKeys { get; set; } = new();

    /// <summary>Gets or sets the JWT bearer options.</summary>
    public JwtAuthOptions Jwt { get; set; } = new();
}

/// <summary>
/// 🔑 Phase 2.7.7 — A single configured API key. The raw key is never stored — only its
/// SHA-256 hash (base64)~ ✨.
/// </summary>
public sealed class ApiKeyEntry
{
    /// <summary>Gets or sets the base64 SHA-256 hash of the raw key.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the caller id this key authenticates as (flows into execution audit).</summary>
    public string CallerId { get; set; } = string.Empty;

    /// <summary>Gets or sets the roles granted to this key.</summary>
    public string[] Roles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// 🎫 Phase 2.7.7 — JWT bearer validation options (<c>Api:Auth:Jwt</c>)~ ✨.
/// </summary>
public sealed class JwtAuthOptions
{
    /// <summary>Gets or sets the token authority (OIDC issuer URL) — optional when a signing key is set.</summary>
    public string? Authority { get; set; }

    /// <summary>Gets or sets the expected token issuer.</summary>
    public string? Issuer { get; set; }

    /// <summary>Gets or sets the expected audience.</summary>
    public string? Audience { get; set; }

    /// <summary>Gets or sets a symmetric signing key (for self/test-issued tokens).</summary>
    public string? SigningKey { get; set; }

    /// <summary>Gets a value indicating whether the JWT scheme is configured at all.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(this.Authority) || !string.IsNullOrWhiteSpace(this.SigningKey);
}
