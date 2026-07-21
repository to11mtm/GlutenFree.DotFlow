// <copyright file="InMemoryLocalStorage.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Scripts.Components;

using System.Collections.Generic;
using System.Threading.Tasks;
using Workflow.UI.Client.Services;

/// <summary>🧪 Phase 3.4 — A tiny in-memory <see cref="ILocalStorage"/> for component tests~ ✨.</summary>
internal sealed class InMemoryLocalStorage : ILocalStorage
{
    public Dictionary<string, string> Store { get; } = new();

    public ValueTask<string?> GetAsync(string key) => new(this.Store.TryGetValue(key, out var v) ? v : null);

    public ValueTask SetAsync(string key, string value)
    {
        this.Store[key] = value;
        return default;
    }

    public ValueTask RemoveAsync(string key)
    {
        this.Store.Remove(key);
        return default;
    }
}
