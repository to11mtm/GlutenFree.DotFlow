// <copyright file="Program.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

using System;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workflow.UI.Client.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// ⚙️ Phase 3.3.a.0 — API options (BaseUrl from appsettings; defaults to the app's own origin)~
var apiOptions = new ApiClientOptions();
builder.Configuration.GetSection("Api").Bind(apiOptions);
if (string.IsNullOrWhiteSpace(apiOptions.BaseUrl))
{
    apiOptions.BaseUrl = builder.HostEnvironment.BaseAddress;
}

builder.Services.AddSingleton(apiOptions);

// 🔐 Auth state + the delegating handler that stamps the credential onto REST calls~
builder.Services.AddSingleton<AuthState>();
builder.Services.AddTransient<AuthMessageHandler>();

// 🌐 A single auth-stamped HttpClient pointed at the API, shared by the typed clients~
builder.Services.AddHttpClient("api", client => client.BaseAddress = new Uri(apiOptions.BaseUrl.TrimEnd('/') + "/"))
    .AddHttpMessageHandler<AuthMessageHandler>();

builder.Services.AddScoped(sp =>
    new WorkflowsClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));
builder.Services.AddScoped(sp =>
    new ModulesClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));
builder.Services.AddScoped(sp =>
    new ExecutionsClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));
builder.Services.AddScoped(sp =>
    new SystemClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("api")));

// 🔔 App services: toasts + localStorage~
builder.Services.AddScoped<Workflow.UI.Client.Services.ToastService>();
builder.Services.AddScoped<Workflow.UI.Client.Services.ILocalStorage, Workflow.UI.Client.Services.BrowserLocalStorage>();

// 📡 Real-time hub client (SignalR)~
builder.Services.AddScoped(sp => new RealTimeClient(
    sp.GetRequiredService<ApiClientOptions>(),
    sp.GetRequiredService<AuthState>()));

await builder.Build().RunAsync();
