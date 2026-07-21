// <copyright file="RenderModes.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Components.Web;

namespace Workflow.UI.Client;

/// <summary>
/// Shared render mode instances for the app's pages.
/// </summary>
public static class RenderModes
{
    /// <summary>
    /// Interactive WebAssembly with prerendering disabled. The app depends on browser-only
    /// services (localStorage, SignalR) that aren't available in the server prerender pass,
    /// and those services are only registered in the WASM client's DI container.
    /// </summary>
    public static readonly InteractiveWebAssemblyRenderMode InteractiveWasm = new(prerender: false);
}
