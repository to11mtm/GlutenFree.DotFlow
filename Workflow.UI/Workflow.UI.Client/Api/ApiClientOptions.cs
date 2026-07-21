// <copyright file="ApiClientOptions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

/// <summary>
/// ⚙️ Phase 3.3.a.0 — Configurable API connection settings (bound from <c>appsettings.json</c>
/// under <c>Api</c>). <see cref="BaseUrl"/> is the API root; the hub URL is derived from it~ ✨.
/// </summary>
public sealed class ApiClientOptions
{
    /// <summary>Gets or sets the API base URL (e.g. <c>https://localhost:7001</c>).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Gets the SignalR hub URL (<see cref="BaseUrl"/> + <c>/hubs/workflow</c>)~ 📡.</summary>
    public string HubUrl => this.Combine("hubs/workflow");

    /// <summary>Combines the base url with a relative path, tolerant of trailing slashes~ 🔗.</summary>
    /// <param name="relative">The relative path (no leading slash needed).</param>
    /// <returns>The absolute URL.</returns>
    public string Combine(string relative)
    {
        var baseUrl = this.BaseUrl.TrimEnd('/');
        return $"{baseUrl}/{relative.TrimStart('/')}";
    }
}
