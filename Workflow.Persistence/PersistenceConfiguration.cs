// <copyright file="PersistenceConfiguration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence;

/// <summary>
/// ⚙️ Configuration for a single persistence provider~ ✨
/// </summary>
public record PersistenceConfiguration
{
    /// <summary>Gets the provider name (e.g. <c>"postgres"</c>, <c>"nats"</c>, <c>"memory"</c>)~ 🏷️.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets the connection string for the provider~ 🔗.</summary>
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>Gets additional provider-specific options~ ⚙️.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Validates this configuration. Throws <see cref="ArgumentException"/> if invalid~ ❌.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderName))
        {
            throw new ArgumentException("ProviderName must not be empty~ 💔", nameof(ProviderName));
        }
    }
}

