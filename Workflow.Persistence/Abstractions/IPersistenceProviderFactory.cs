// <copyright file="IPersistenceProviderFactory.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Persistence.Abstractions;

/// <summary>
/// 🏭 Factory for creating persistence providers from configuration~ ✨
/// </summary>
public interface IPersistenceProviderFactory
{
    /// <summary>Creates a persistence provider from the given configuration~ 🔧.</summary>
    IPersistenceProvider Create(PersistenceConfiguration config);

    /// <summary>Returns whether this factory can create a provider for the given name~ ❓.</summary>
    bool CanHandle(string providerName);
}

