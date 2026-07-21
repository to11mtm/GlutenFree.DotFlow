// <copyright file="RequestBodyEncoder.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http.Internal;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;

/// <summary>
/// 📤 Phase 2.3.1 — Encodes a workflow body object into an <see cref="HttpContent"/> by
/// dispatching on the requested <c>contentType</c>~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: V1 strategies (one method per branch — small enough to keep inline,
/// large enough to be unit-testable in isolation):
/// </para>
/// <list type="bullet">
///   <item><description><c>application/json</c> — JSON-serialise objects, pass strings through.</description></item>
///   <item><description><c>application/x-www-form-urlencoded</c> — encode dictionary as key=value pairs.</description></item>
///   <item><description><c>multipart/form-data</c> — V1 only supports <c>byte[]</c> parts (per Q5).</description></item>
///   <item><description><c>application/xml</c> / <c>text/xml</c> — string passthrough + well-formedness check.</description></item>
///   <item><description><c>text/plain</c> — string passthrough.</description></item>
///   <item><description><c>application/octet-stream</c> — byte-array passthrough.</description></item>
/// </list>
/// <para>
/// When <c>contentType</c> is null/empty, defaults to JSON for objects, octet-stream for byte[],
/// text/plain for strings~ 🧠
/// </para>
/// </remarks>
public static class RequestBodyEncoder
{
    /// <summary>
    /// Result of an encode attempt — success carries the <see cref="HttpContent"/>,
    /// failure carries a human-readable error message~ 💔.
    /// </summary>
    /// <param name="Content">The encoded body, or <c>null</c> on failure.</param>
    /// <param name="Error">The error message, or <c>null</c> on success.</param>
    public readonly record struct EncodeResult(HttpContent? Content, string? Error)
    {
        /// <summary>Was the encode successful?.</summary>
        public bool IsSuccess => Content is not null;

        public static EncodeResult Ok(HttpContent content) => new(content, null);

        public static EncodeResult Fail(string error) => new(null, error);
    }

    /// <summary>
    /// Encode <paramref name="body"/> into an <see cref="HttpContent"/> targeting
    /// <paramref name="contentType"/>~ 📤.
    /// </summary>
    /// <param name="body">The body payload (dictionary, string, byte[], list, POCO, …).</param>
    /// <param name="contentType">Requested MIME type, or null/empty to auto-pick by body shape.</param>
    /// <returns>An <see cref="EncodeResult"/> with either content or an error.</returns>
    public static EncodeResult Encode(object body, string? contentType)
    {
        var ct = contentType?.Trim().ToLowerInvariant() ?? string.Empty;

        // No explicit content type → auto-pick based on body shape~ 🧠
        if (string.IsNullOrEmpty(ct))
        {
            return body switch
            {
                string s => EncodeResult.Ok(new StringContent(s, Encoding.UTF8, "text/plain")),
                byte[] bytes => OctetStream(bytes),
                _ => EncodeJson(body),
            };
        }

        // Explicit strategies — try the most specific match first~ 🎯
        if (ct.StartsWith("application/json", StringComparison.Ordinal) || ct.EndsWith("+json", StringComparison.Ordinal))
        {
            return EncodeJson(body);
        }

        if (ct.StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal))
        {
            return EncodeFormUrlEncoded(body);
        }

        if (ct.StartsWith("multipart/form-data", StringComparison.Ordinal))
        {
            return EncodeMultipart(body);
        }

        if (ct.StartsWith("application/xml", StringComparison.Ordinal) || ct.StartsWith("text/xml", StringComparison.Ordinal))
        {
            return EncodeXml(body, ct);
        }

        if (ct.StartsWith("text/", StringComparison.Ordinal))
        {
            return EncodeText(body, ct);
        }

        if (ct.StartsWith("application/octet-stream", StringComparison.Ordinal))
        {
            return body switch
            {
                byte[] bytes => OctetStream(bytes),
                string s => OctetStream(Encoding.UTF8.GetBytes(s)),
                _ => EncodeResult.Fail($"application/octet-stream requires byte[] or string body (got {body.GetType().Name})~ 💔"),
            };
        }

        // Unknown content type — best effort: stringify and stamp it~ 🤷
        var fallback = new StringContent(body is string s2 ? s2 : JsonSerializer.Serialize(body), Encoding.UTF8);
        fallback.Headers.ContentType = new MediaTypeHeaderValue(contentType!);
        return EncodeResult.Ok(fallback);
    }

    /// <summary>JSON-encode (objects → System.Text.Json; strings pass through as already-JSON)~ 📦.</summary>
    private static EncodeResult EncodeJson(object body)
    {
        var json = body is string s ? s : JsonSerializer.Serialize(body);
        return EncodeResult.Ok(new StringContent(json, Encoding.UTF8, "application/json"));
    }

    /// <summary>Encode an <see cref="IDictionary{TKey,TValue}"/> as <c>application/x-www-form-urlencoded</c>~ 🔗.</summary>
    private static EncodeResult EncodeFormUrlEncoded(object body)
    {
        var dict = TryCoerceStringDictionary(body);
        if (dict is null)
        {
            return EncodeResult.Fail(
                $"application/x-www-form-urlencoded requires a dictionary body (got {body.GetType().Name})~ 💔");
        }

        // FormUrlEncodedContent has a 2048-char URL-segment limit per pair in some old impls;
        // we use the standard ctor which handles arbitrary lengths via POST body~
        return EncodeResult.Ok(new FormUrlEncodedContent(dict));
    }

    /// <summary>
    /// Encode a dictionary of parts as <c>multipart/form-data</c>~ 📎.
    /// V1: each part value must be a <c>byte[]</c> (raw bytes) or a string (plain text part).
    /// Stream/file-path support deferred to 2.3.P4/P5~
    /// </summary>
    private static EncodeResult EncodeMultipart(object body)
    {
        var multipart = new MultipartFormDataContent();

        switch (body)
        {
            case IDictionary<string, object?> parts:
                foreach (var kv in parts)
                {
                    var addResult = AddMultipartPart(multipart, kv.Key, kv.Value);
                    if (addResult is not null)
                    {
                        multipart.Dispose();
                        return EncodeResult.Fail(addResult);
                    }
                }

                break;
            case System.Collections.IDictionary nd:
                foreach (System.Collections.DictionaryEntry entry in nd)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    var addResult = AddMultipartPart(multipart, key, entry.Value);
                    if (addResult is not null)
                    {
                        multipart.Dispose();
                        return EncodeResult.Fail(addResult);
                    }
                }

                break;
            default:
                multipart.Dispose();
                return EncodeResult.Fail(
                    $"multipart/form-data requires a dictionary body (got {body.GetType().Name})~ 💔");
        }

        return EncodeResult.Ok(multipart);
    }

    /// <summary>Add a single multipart part. Returns an error message on rejection, null on success~ 📎.</summary>
    private static string? AddMultipartPart(MultipartFormDataContent multipart, string name, object? value)
    {
        switch (value)
        {
            case null:
                multipart.Add(new StringContent(string.Empty), name);
                return null;
            case byte[] bytes:
                var bc = new ByteArrayContent(bytes);
                bc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                multipart.Add(bc, name, name); // filename = name fallback for the byte[] case~
                return null;
            case string s:
                multipart.Add(new StringContent(s, Encoding.UTF8, "text/plain"), name);
                return null;
            default:
                // Stream/file-path support tracked in 2.3.P4/P5~ ❌
                return $"multipart part '{name}' must be byte[] or string in V1 (got {value.GetType().Name})~ 💔";
        }
    }

    /// <summary>XML passthrough — validates well-formedness so bad XML fails *before* the request~ 🧪.</summary>
    private static EncodeResult EncodeXml(object body, string contentType)
    {
        if (body is not string xml)
        {
            return EncodeResult.Fail(
                $"{contentType} requires a string body (XML auto-serialisation deferred)~ 💔");
        }

        // Well-formedness check — fail fast before sending~ 🛡️
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
        }
        catch (XmlException ex)
        {
            return EncodeResult.Fail($"XML body is not well-formed: {ex.Message}~ 💔");
        }

        return EncodeResult.Ok(new StringContent(xml, Encoding.UTF8, contentType));
    }

    /// <summary>Plain text encoding (any <c>text/*</c> family)~ 📝.</summary>
    private static EncodeResult EncodeText(object body, string contentType)
    {
        var text = body switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => body.ToString() ?? string.Empty,
        };

        return EncodeResult.Ok(new StringContent(text, Encoding.UTF8, contentType));
    }

    /// <summary>Raw bytes wrapped as <c>application/octet-stream</c>~ 📦.</summary>
    private static EncodeResult OctetStream(byte[] bytes)
    {
        var bc = new ByteArrayContent(bytes);
        bc.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return EncodeResult.Ok(bc);
    }

    /// <summary>Coerce any dictionary-ish input into <c>IDictionary&lt;string,string&gt;</c> (null on failure)~ 🔧.</summary>
    private static IDictionary<string, string>? TryCoerceStringDictionary(object body)
    {
        switch (body)
        {
            case IDictionary<string, string> sd:
                return sd;
            case IDictionary<string, object?> od:
                return od.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty);
            case System.Collections.IDictionary nd:
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry entry in nd)
                {
                    result[entry.Key?.ToString() ?? string.Empty] = entry.Value?.ToString() ?? string.Empty;
                }

                return result;
            default:
                return null;
        }
    }
}

