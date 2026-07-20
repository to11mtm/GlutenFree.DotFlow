// <copyright file="ILocalStorage.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Services;

using System.Threading.Tasks;
using Microsoft.JSInterop;

/// <summary>
/// 💾 Phase 3.3.a.1 — A minimal browser <c>localStorage</c> seam so components can persist small
/// values (e.g. the auth token) and tests can substitute an in-memory fake~ ✨.
/// </summary>
public interface ILocalStorage
{
    /// <summary>Gets a stored string value, or null~ 💾.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or null.</returns>
    ValueTask<string?> GetAsync(string key);

    /// <summary>Sets a stored string value~ 💾.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A task.</returns>
    ValueTask SetAsync(string key, string value);

    /// <summary>Removes a stored value~ 🗑️.</summary>
    /// <param name="key">The key.</param>
    /// <returns>A task.</returns>
    ValueTask RemoveAsync(string key);
}

/// <summary>💾 Phase 3.3.a.1 — <see cref="ILocalStorage"/> backed by browser <c>localStorage</c> via JS interop~ ✨.</summary>
public sealed class BrowserLocalStorage : ILocalStorage
{
    private readonly IJSRuntime js;

    /// <summary>Initializes a new instance of the <see cref="BrowserLocalStorage"/> class~ 💾.</summary>
    /// <param name="js">The JS runtime.</param>
    public BrowserLocalStorage(IJSRuntime js) => this.js = js;

    /// <inheritdoc/>
    public ValueTask<string?> GetAsync(string key) => this.js.InvokeAsync<string?>("localStorage.getItem", key);

    /// <inheritdoc/>
    public ValueTask SetAsync(string key, string value) => this.js.InvokeVoidAsync("localStorage.setItem", key, value);

    /// <inheritdoc/>
    public ValueTask RemoveAsync(string key) => this.js.InvokeVoidAsync("localStorage.removeItem", key);
}
