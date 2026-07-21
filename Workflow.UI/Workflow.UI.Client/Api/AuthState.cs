// <copyright file="AuthState.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;

/// <summary>
/// 🔐 Phase 3.3.a.0 — Holds the caller's credential (JWT bearer or API key) for the session
/// (D9). Framework-free: persistence to <c>localStorage</c> is done by the settings component,
/// not here. The <see cref="AuthMessageHandler"/> and <see cref="RealTimeClient"/> read from it~ ✨.
/// </summary>
public sealed class AuthState
{
    private string? token;
    private string? apiKey;

    /// <summary>Raised whenever the credential changes~ 🔔.</summary>
    public event Action? Changed;

    /// <summary>Gets the current JWT bearer token, if any.</summary>
    public string? Token => this.token;

    /// <summary>Gets the current API key, if any.</summary>
    public string? ApiKey => this.apiKey;

    /// <summary>Gets a value indicating whether any credential is present.</summary>
    public bool HasCredential => !string.IsNullOrWhiteSpace(this.token) || !string.IsNullOrWhiteSpace(this.apiKey);

    /// <summary>Sets a JWT bearer token (clears any API key)~ 🎫.</summary>
    /// <param name="value">The token, or null/empty to clear.</param>
    public void SetToken(string? value)
    {
        this.token = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (this.token is not null)
        {
            this.apiKey = null;
        }

        this.Changed?.Invoke();
    }

    /// <summary>Sets an API key (clears any bearer token)~ 🔑.</summary>
    /// <param name="value">The key, or null/empty to clear.</param>
    public void SetApiKey(string? value)
    {
        this.apiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (this.apiKey is not null)
        {
            this.token = null;
        }

        this.Changed?.Invoke();
    }

    /// <summary>Clears all credentials~ 🚪.</summary>
    public void Clear()
    {
        this.token = null;
        this.apiKey = null;
        this.Changed?.Invoke();
    }
}
