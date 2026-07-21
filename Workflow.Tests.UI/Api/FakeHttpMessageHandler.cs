// <copyright file="FakeHttpMessageHandler.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 🧪 Phase 3.3.a.0 — A scriptable <see cref="HttpMessageHandler"/> for testing the API clients
/// without a real server: capture requests, return canned responses~ ✨.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

    /// <summary>Initializes a new instance of the <see cref="FakeHttpMessageHandler"/> class~ 🧪.</summary>
    /// <param name="responder">Produces a response for each request.</param>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => this.responder = responder;

    /// <summary>The requests this handler has seen (in order)~ 📋.</summary>
    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>The captured string bodies of requests that had content~ 📋.</summary>
    public List<string> Bodies { get; } = new();

    /// <summary>Convenience: a handler that always returns the given JSON with 200 OK~ 🧪.</summary>
    /// <param name="json">The response JSON.</param>
    /// <returns>A configured handler.</returns>
    public static FakeHttpMessageHandler Json(string json)
        => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        this.Requests.Add(request);
        if (request.Content is not null)
        {
            this.Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        else
        {
            this.Bodies.Add(string.Empty);
        }

        return this.responder(request);
    }

    /// <summary>Builds an <see cref="HttpClient"/> using this handler with the given base URL~ 🌐.</summary>
    /// <param name="baseUrl">The base URL.</param>
    /// <returns>The client.</returns>
    public HttpClient CreateClient(string baseUrl = "http://localhost/")
        => new(this) { BaseAddress = new Uri(baseUrl) };
}
