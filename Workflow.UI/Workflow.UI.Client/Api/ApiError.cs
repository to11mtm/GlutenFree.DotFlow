// <copyright file="ApiError.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.UI.Client.Api;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 💥 Phase 3.3.a.0 — A normalized API error surfaced from a failed request. Wraps RFC 7807
/// ProblemDetails (the 2.7 `ApiResults` convention) plus transport failures~ ✨.
/// </summary>
/// <param name="StatusCode">The HTTP status code (0 for transport/connection failures).</param>
/// <param name="Title">A short human-readable title.</param>
/// <param name="Detail">Optional detail text.</param>
/// <param name="Errors">Optional field → messages map (validation).</param>
public sealed record ApiError(
    int StatusCode,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    /// <summary>Renders a single-line summary for toasts/dialogs~ 📝.</summary>
    /// <returns>The summary text.</returns>
    public string Summarize()
        => string.IsNullOrWhiteSpace(this.Detail) ? this.Title : $"{this.Title}: {this.Detail}";
}

/// <summary>💥 Phase 3.3.a.0 — Thrown by the API clients when a request fails~ ✨.</summary>
public sealed class ApiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ApiException"/> class~ 💥.</summary>
    /// <param name="error">The normalized error.</param>
    public ApiException(ApiError error)
        : base(error.Summarize())
        => this.Error = error;

    /// <summary>Gets the normalized error.</summary>
    public ApiError Error { get; }
}

/// <summary>
/// 🌐 Phase 3.3.a.0 — Shared HTTP helpers for the typed API clients: JSON options + a
/// ProblemDetails-aware response reader that throws <see cref="ApiException"/> on failure~ ✨.
/// </summary>
public static class ApiHttp
{
    /// <summary>Web-default (camelCase) JSON options used across all clients~ 🔤.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends a request and deserializes a success body, else throws <see cref="ApiException"/>~ 🌐.</summary>
    /// <typeparam name="T">The expected body type.</typeparam>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized body.</returns>
    public static async Task<T> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        var response = await SendRawAsync(client, request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
        return body!;
    }

    /// <summary>Sends a request expecting no body, else throws <see cref="ApiException"/>~ 🌐.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task.</returns>
    public static async Task SendNoContentAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
        => await SendRawAsync(client, request, ct).ConfigureAwait(false);

    /// <summary>Sends a request, throwing a normalized <see cref="ApiException"/> on non-success~ 🌐.</summary>
    /// <param name="client">The HTTP client.</param>
    /// <param name="request">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The successful response.</returns>
    public static async Task<HttpResponseMessage> SendRawAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(new ApiError(0, "Cannot reach the API", ex.Message));
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        throw new ApiException(await ReadProblemAsync(response, ct).ConfigureAwait(false));
    }

    private static async Task<ApiError> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        try
        {
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false);
            var title = doc.TryGetProperty("title", out var t) ? t.GetString() ?? DefaultTitle(response.StatusCode) : DefaultTitle(response.StatusCode);
            var detail = doc.TryGetProperty("detail", out var d) ? d.GetString() : null;

            IReadOnlyDictionary<string, string[]>? errors = null;
            if (doc.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
                foreach (var prop in e.EnumerateObject())
                {
                    var list = new List<string>();
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                list.Add(item.GetString()!);
                            }
                        }
                    }

                    map[prop.Name] = list.ToArray();
                }

                errors = map;
            }

            return new ApiError(status, title, detail, errors);
        }
        catch (Exception)
        {
            return new ApiError(status, DefaultTitle(response.StatusCode));
        }
    }

    private static string DefaultTitle(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => "Authentication required",
        HttpStatusCode.Forbidden => "Not permitted",
        HttpStatusCode.NotFound => "Not found",
        HttpStatusCode.Conflict => "Conflict",
        HttpStatusCode.UnprocessableEntity => "Validation failed",
        HttpStatusCode.ServiceUnavailable => "Service unavailable",
        _ => $"Request failed ({(int)code})",
    };
}
