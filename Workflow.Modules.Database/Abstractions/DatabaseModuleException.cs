// <copyright file="DatabaseModuleException.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Database.Abstractions;

using System;

/// <summary>
/// 🚨 Base exception for everything thrown by the database module family~
/// Catch this to handle any database-infrastructure failure in one place! 💖.
/// </summary>
/// <remarks>
/// CopilotNote: Phase 2.4.a.0 — all shared-infra exceptions inherit from this base
/// so modules can distinguish "our plumbing failed" from provider-level SQL errors
/// (which surface as <c>Npgsql</c>/<c>Sqlite</c> exceptions wrapped in ModuleResult.Fail)~ 🌸.
/// </remarks>
public class DatabaseModuleException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseModuleException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public DatabaseModuleException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseModuleException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DatabaseModuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 🗂️ Thrown by <see cref="IDbProviderRegistry"/> when a provider key isn't registered~.
/// </summary>
public sealed class UnknownProviderException : DatabaseModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownProviderException"/> class.
    /// </summary>
    /// <param name="providerKey">The unrecognised provider key.</param>
    public UnknownProviderException(string providerKey)
        : base($"Unknown database provider key '{providerKey}'. Register it via IDbProviderRegistry or use a known provider~")
    {
        this.ProviderKey = providerKey;
    }

    /// <summary>
    /// Gets the provider key that failed to resolve. 🔑.
    /// </summary>
    public string ProviderKey { get; }
}

/// <summary>
/// 📇 Thrown by <see cref="IDbConnectionFactory"/> when a named connection id isn't registered~.
/// </summary>
public sealed class ConnectionNotFoundException : DatabaseModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionNotFoundException"/> class.
    /// </summary>
    /// <param name="connectionId">The unknown connection id.</param>
    public ConnectionNotFoundException(string connectionId)
        : base($"Named database connection '{connectionId}' was not found in the connection registry~")
    {
        this.ConnectionId = connectionId;
    }

    /// <summary>
    /// Gets the connection id that failed to resolve. 🆔.
    /// </summary>
    public string ConnectionId { get; }
}

/// <summary>
/// 🧷 Thrown by the SQL parameter binder when a parameter value's type can't be mapped
/// to a supported provider type (D7 — parameterisation is mandatory, no string concat!)~.
/// </summary>
public sealed class SqlParameterBindingException : DatabaseModuleException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqlParameterBindingException"/> class.
    /// </summary>
    /// <param name="paramName">The parameter name that failed to bind.</param>
    /// <param name="reason">Why the binding failed.</param>
    public SqlParameterBindingException(string paramName, string reason)
        : base($"Cannot bind SQL parameter '{paramName}': {reason}")
    {
        this.ParamName = paramName;
        this.Reason = reason;
    }

    /// <summary>
    /// Gets the parameter name that failed to bind. 🏷️.
    /// </summary>
    public string ParamName { get; }

    /// <summary>
    /// Gets the reason the binding failed. 📝.
    /// </summary>
    public string Reason { get; }
}

