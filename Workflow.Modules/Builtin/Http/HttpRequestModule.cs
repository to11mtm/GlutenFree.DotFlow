// <copyright file="HttpRequestModule.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Modules.Builtin.Http;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin.Http.Auth;
using Workflow.Modules.Builtin.Http.Internal;
using Workflow.Modules.Builtin.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;

/// <summary>
/// 🌐 Built-in HTTP request module (<c>builtin.http.request</c>) — Phase 2.3.0 v1~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// V1 minimal slice: all standard HTTP methods, JSON body in / JSON-or-string body out,
/// arbitrary headers, per-request timeout. Auth, retries, transformations, multipart, etc.
/// are layered on top in 2.3.1–2.3.5~ 🌸
/// </para>
/// <para>
/// CopilotNote: We resolve <see cref="IHttpClientFactory"/> lazily from
/// <see cref="ModuleExecutionContext.Services"/> so the module stays parameterless-constructable
/// (matches <c>BuiltinModules.GetAll()</c> + <c>ModuleDiscovery</c> conventions). Hosts must
/// wire up the factory via <see cref="WorkflowModulesServiceCollectionExtensions.AddWorkflowModules"/>
/// (or <see cref="HttpModuleServiceCollectionExtensions.AddHttpModules"/> for fine-grained control)~ 🧠
/// </para>
/// </remarks>
public class HttpRequestModule : IWorkflowModule
{
    /// <summary>
    /// Known HTTP verbs supported by the v1 schema~ 🏷️.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> _knownMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS",
    };

    /// <summary>
    /// 🔑 Per-module OAuth2 token cache — fresh dictionary tied to *this* module instance lifetime.
    /// Used when <c>oauth2TokenCacheScope = module</c> (the default scope)~
    /// </summary>
    /// <remarks>
    /// CopilotNote: Pipeline-scope cache lives in DI (<see cref="PerPipelineOAuth2TokenCache"/>) and
    /// is resolved per-call via <see cref="ModuleExecutionContext.Services"/>~ 🌸
    /// </remarks>
    private readonly PerModuleOAuth2TokenCache _oauth2ModuleCache = new();

    /// <summary>
    /// 🔄 Per-module Polly resilience pipeline factory — caches built pipelines per config-hash so
    /// circuit breakers maintain state across calls within the same module instance lifetime~
    /// </summary>
    private readonly HttpResiliencePipelineFactory _resilienceFactory = new();

    /// <inheritdoc />
    public string ModuleId => "builtin.http.request";

    /// <inheritdoc />
    public string DisplayName => "HTTP Request";

    /// <inheritdoc />
    public string Category => "Network";

    /// <inheritdoc />
    public string Description => "Sends an outbound HTTP request and returns the response~ 🌐✨";

    /// <inheritdoc />
    public string Icon => "🌐";

    /// <inheritdoc />
    public Version Version => new(1, 0, 0);

    /// <inheritdoc />
    public ModuleSchema Schema => new(
        Inputs: Arr<PortDefinition>.Empty,
        Outputs: Arr.create(
            new PortDefinition(
                Name: "statusCode",
                DisplayName: "Status Code",
                DataType: typeof(int),
                Description: "HTTP response status code (e.g. 200, 404)~ 🔢",
                IsRequired: false),
            new PortDefinition(
                Name: "headers",
                DisplayName: "Response Headers",
                DataType: typeof(Dictionary<string, string>),
                Description: "Flattened response headers (comma-joined for multi-value)~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "body",
                DisplayName: "Response Body",
                DataType: typeof(object),
                Description: "Decoded response body — object for JSON, string otherwise~ 📦",
                IsRequired: false),
            new PortDefinition(
                Name: "success",
                DisplayName: "Success",
                DataType: typeof(bool),
                Description: "True when status is 200–299~ ✅",
                IsRequired: false),
            new PortDefinition(
                Name: "durationMs",
                DisplayName: "Duration (ms)",
                DataType: typeof(long),
                Description: "Round-trip elapsed time in milliseconds~ ⏱️",
                IsRequired: false),
            new PortDefinition(
                Name: "contentType",
                DisplayName: "Response Content-Type",
                DataType: typeof(string),
                Description: "Response Content-Type media type — useful for downstream switching~ 🏷️",
                IsRequired: false),
            new PortDefinition(
                Name: "attemptCount",
                DisplayName: "Attempt Count",
                DataType: typeof(int),
                Description: "Total send attempts (1 = no retry; 2 = retried once, etc.)~ 🔢",
                IsRequired: false),
            new PortDefinition(
                Name: "circuitState",
                DisplayName: "Circuit State",
                DataType: typeof(string),
                Description: "Current circuit-breaker state: closed/open/halfopen (when circuit configured)~ 🚦",
                IsRequired: false)),
        Properties: Arr.create(
            new ModulePropertyDefinition(
                Name: "url",
                DisplayName: "URL",
                DataType: typeof(string),
                Description: "Absolute request URL. Supports {{Variable.Name}} references~ 🌐",
                IsRequired: true,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "method",
                DisplayName: "Method",
                DataType: typeof(string),
                Description: "HTTP method: GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS~ 🏷️",
                IsRequired: false,
                DefaultValue: "GET",
                EditorType: PropertyEditorType.Dropdown),
            new ModulePropertyDefinition(
                Name: "headers",
                DisplayName: "Headers",
                DataType: typeof(Dictionary<string, string>),
                Description: "Optional request headers (key → value)~ 🏷️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "body",
                DisplayName: "Body",
                DataType: typeof(object),
                Description: "Optional request body — serialised as JSON by default~ 📦",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "contentType",
                DisplayName: "Content-Type",
                DataType: typeof(string),
                Description: "Optional request body media type (e.g. application/json, application/x-www-form-urlencoded, multipart/form-data, application/xml, text/plain, application/octet-stream). Auto-picks JSON for objects when omitted~ 🏷️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "timeoutSeconds",
                DisplayName: "Timeout (seconds)",
                DataType: typeof(int),
                Description: "Per-request timeout in seconds (default 30)~ ⏱️",
                IsRequired: false,
                DefaultValue: 30,
                EditorType: PropertyEditorType.Number),

            // 🔐 Phase 2.3.2 — Authentication properties~
            new ModulePropertyDefinition(
                Name: "authType",
                DisplayName: "Auth Type",
                DataType: typeof(string),
                Description: "Authentication scheme: none/basic/bearer/apikey/oauth2 (oauth2 ships in 2.3.3)~ 🔐",
                IsRequired: false,
                DefaultValue: "none",
                EditorType: PropertyEditorType.Dropdown),
            new ModulePropertyDefinition(
                Name: "username",
                DisplayName: "Username",
                DataType: typeof(string),
                Description: "Basic auth — username~ 👤",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "password",
                DisplayName: "Password",
                DataType: typeof(string),
                Description: "Basic auth — password~ 🔑",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "bearerToken",
                DisplayName: "Bearer Token",
                DataType: typeof(string),
                Description: "Bearer auth — token to send in Authorization header~ 🎟️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "apiKey",
                DisplayName: "API Key",
                DataType: typeof(string),
                Description: "API key value~ 🗝️",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "apiKeyHeader",
                DisplayName: "API Key Header Name",
                DataType: typeof(string),
                Description: "Header (or query param) name for the API key (default X-API-Key)~ 🏷️",
                IsRequired: false,
                DefaultValue: "X-API-Key",
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "apiKeyLocation",
                DisplayName: "API Key Location",
                DataType: typeof(string),
                Description: "Where to put the API key: 'header' (default) or 'query'~ 📍",
                IsRequired: false,
                DefaultValue: "header",
                EditorType: PropertyEditorType.Dropdown),

            // 🔑 Phase 2.3.3 — OAuth2 Client Credentials properties~
            new ModulePropertyDefinition(
                Name: "oauth2TokenUrl",
                DisplayName: "OAuth2 Token URL",
                DataType: typeof(string),
                Description: "OAuth2 token endpoint URL (e.g. https://auth.example.com/oauth/token)~ 🌐",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "oauth2ClientId",
                DisplayName: "OAuth2 Client ID",
                DataType: typeof(string),
                Description: "OAuth2 client identifier~ 👤",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "oauth2ClientSecret",
                DisplayName: "OAuth2 Client Secret",
                DataType: typeof(string),
                Description: "OAuth2 client secret~ 🔑",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "oauth2Scope",
                DisplayName: "OAuth2 Scope",
                DataType: typeof(string),
                Description: "Optional space-separated OAuth2 scopes~ 🎯",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "oauth2Audience",
                DisplayName: "OAuth2 Audience",
                DataType: typeof(string),
                Description: "Optional audience parameter (Auth0-style; not part of core RFC)~ 🎭",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Text),
            new ModulePropertyDefinition(
                Name: "oauth2TokenCacheScope",
                DisplayName: "OAuth2 Token Cache Scope",
                DataType: typeof(string),
                Description: "Cache scope: 'module' (default — per HttpRequestModule instance) or 'pipeline' (shared across nodes in same WorkflowExecution). Singleton/persisted ship in 2.3.P3~ 💾",
                IsRequired: false,
                DefaultValue: "module",
                EditorType: PropertyEditorType.Dropdown),

            // 🔄 Phase 2.3.4 — Resilience properties (Polly v8 retry / circuit-breaker)~
            new ModulePropertyDefinition(
                Name: "retryCount",
                DisplayName: "Retry Count",
                DataType: typeof(int),
                Description: "Number of retry attempts after the initial send (0 = no retries)~ 🔁",
                IsRequired: false,
                DefaultValue: 0,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "retryBackoff",
                DisplayName: "Retry Backoff Strategy",
                DataType: typeof(string),
                Description: "Backoff curve: linear/exponential/constant (default exponential)~ 📈",
                IsRequired: false,
                DefaultValue: "exponential",
                EditorType: PropertyEditorType.Dropdown),
            new ModulePropertyDefinition(
                Name: "retryDelaySeconds",
                DisplayName: "Retry Initial Delay (s)",
                DataType: typeof(double),
                Description: "Initial backoff delay in seconds (default 1.0). Subsequent attempts scale per the chosen backoff~ ⏱️",
                IsRequired: false,
                DefaultValue: 1.0,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "maxRetryBackoffSeconds",
                DisplayName: "Max Retry Backoff (s)",
                DataType: typeof(double),
                Description: "Hard cap on any per-attempt sleep — also caps server-supplied Retry-After header (default 60s)~ 🛡️",
                IsRequired: false,
                DefaultValue: 60.0,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "retryOnStatusCodes",
                DisplayName: "Retry On Status Codes",
                DataType: typeof(int[]),
                Description: "Status codes that trigger a retry (default [408,429,500,502,503,504])~ 📋",
                IsRequired: false,
                DefaultValue: null,
                EditorType: PropertyEditorType.Json),
            new ModulePropertyDefinition(
                Name: "circuitBreakerFailureThreshold",
                DisplayName: "Circuit Breaker Threshold",
                DataType: typeof(int),
                Description: "Number of failures within the sampling window that trip the circuit (0 = disabled)~ 🛑",
                IsRequired: false,
                DefaultValue: 0,
                EditorType: PropertyEditorType.Number),
            new ModulePropertyDefinition(
                Name: "circuitBreakerSamplingDurationSeconds",
                DisplayName: "Circuit Breaker Window (s)",
                DataType: typeof(double),
                Description: "Sliding window for failure counting (default 30s)~ ⏰",
                IsRequired: false,
                DefaultValue: 30.0,
                EditorType: PropertyEditorType.Number)));

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IReadOnlyDictionary<string, object?> configuration)
    {
        var errors = new List<ValidationError>();

        // Method validation~ 🏷️
        if (configuration.TryGetValue("method", out var methodObj) && methodObj is string methodStr
            && !string.IsNullOrWhiteSpace(methodStr) && !_knownMethods.Contains(methodStr))
        {
            errors.Add(new ValidationError(
                "INVALID_HTTP_METHOD",
                $"Unknown HTTP method '{methodStr}'. Valid: {string.Join(", ", _knownMethods)}~ 💔",
                PropertyName: "method"));
        }

        // URL validation — only when statically provided (skip template strings with {{ }})~ 🌐
        if (configuration.TryGetValue("url", out var urlObj) && urlObj is string urlStr
            && !string.IsNullOrWhiteSpace(urlStr) && !urlStr.Contains("{{", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(urlStr, UriKind.Absolute, out _))
            {
                errors.Add(new ValidationError(
                    "INVALID_URL",
                    $"url '{urlStr}' is not a well-formed absolute URI~ 💔",
                    PropertyName: "url"));
            }
        }

        // Timeout validation~ ⏱️
        var timeout = TryParseInt(configuration, "timeoutSeconds");
        if (timeout.HasValue && timeout.Value <= 0)
        {
            errors.Add(new ValidationError(
                "INVALID_TIMEOUT",
                $"timeoutSeconds must be > 0 (got {timeout.Value})~ 💔",
                PropertyName: "timeoutSeconds"));
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // 1️⃣ Resolve IHttpClientFactory from DI~ 🧠
        var factory = context.Services.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
        if (factory is null)
        {
            return ModuleResult.Fail(
                $"IHttpClientFactory not registered in DI. Call services.AddWorkflowModules() (or AddHttpModules()) at host startup~ 💔");
        }

        // 2️⃣ Read & validate properties~ 📋
        var url = GetString(context.Properties, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return ModuleResult.Fail("url property is required~ 💔");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ModuleResult.Fail($"url '{url}' is not a well-formed absolute URI~ 💔");
        }

        var methodStr = GetString(context.Properties, "method") ?? "GET";
        if (!_knownMethods.Contains(methodStr))
        {
            return ModuleResult.Fail($"Unknown HTTP method '{methodStr}'~ 💔");
        }

        var timeoutSeconds = TryParseInt(context.Properties, "timeoutSeconds") ?? 30;
        if (timeoutSeconds <= 0)
        {
            return ModuleResult.Fail($"timeoutSeconds must be > 0 (got {timeoutSeconds})~ 💔");
        }

        // 3️⃣ Build the request — extracted into a local function so it can be rebuilt for 401-retry~ 🛠️
        var client = factory.CreateClient(HttpModuleServiceCollectionExtensions.HttpClientName);
        var headers = ExtractHeaderMap(context.Properties, "headers");
        var requestedContentType = GetString(context.Properties, "contentType");
        var hasBody = context.Properties.TryGetValue("body", out var bodyObj)
            && bodyObj is not null
            && !IsMethodWithoutBody(methodStr);

        HttpRequestMessage BuildRequest()
        {
            var req = new HttpRequestMessage(new HttpMethod(methodStr.ToUpperInvariant()), uri);
            if (headers is not null)
            {
                foreach (var kv in headers)
                {
                    if (!req.Headers.TryAddWithoutValidation(kv.Key, kv.Value))
                    {
                        context.Logger.LogDebug("🏷️ Header '{Name}' deferred to content (will attach with body)~", kv.Key);
                    }
                }
            }

            if (hasBody)
            {
                var encodeResult = RequestBodyEncoder.Encode(bodyObj!, requestedContentType);
                if (!encodeResult.IsSuccess)
                {
                    throw new InvalidOperationException($"Body encode failed: {encodeResult.Error}");
                }

                req.Content = encodeResult.Content!;

                if (headers is not null)
                {
                    foreach (var kv in headers)
                    {
                        if (IsContentHeader(kv.Key))
                        {
                            req.Content.Headers.Remove(kv.Key);
                            req.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                    }
                }
            }

            return req;
        }

        // 4️⃣ Send with linked CTS for timeout + parent cancellation~ ⏱️🛑
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // 🔐 Phase 2.3.2 — Apply auth strategy (mutates headers and/or URI)~
        // Phase 2.3.3 — pass context + per-module OAuth2 cache so the factory can build OAuth2 strategies~
        var strategy = HttpAuthStrategyFactory.FromProperties(context.Properties, context, _oauth2ModuleCache, out var authError);
        if (strategy is null)
        {
            return ModuleResult.Fail(authError ?? "Failed to configure auth strategy~ 💔");
        }

        // 🔄 Phase 2.3.4 — Parse + cache the Polly resilience pipeline (retry / circuit breaker)~
        var resilienceConfig = HttpResiliencePipelineFactory.ParseFromProperties(context.Properties, out var resilienceError);
        if (resilienceConfig is null)
        {
            return ModuleResult.Fail(resilienceError ?? "Failed to configure resilience pipeline~ 💔");
        }

        var resiliencePipeline = _resilienceFactory.GetOrBuild(resilienceConfig, context.Logger);
        var callState = new ResilienceCallState();

        var sw = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        try
        {
            // First send: pipeline handles retry/circuit-breaker; each attempt rebuilds the request + re-applies auth
            response = await SendThroughPipelineAsync(
                client,
                resiliencePipeline,
                callState,
                BuildRequest,
                strategy,
                context,
                timeoutCts.Token).ConfigureAwait(false);

            // 🔄 Phase 2.3.3 — Refresh-on-401 retry (one attempt only, OUTSIDE the resilience pipeline)~
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                bool shouldRetry = await strategy.InvalidateAndPrepareRetryAsync(context, timeoutCts.Token)
                    .ConfigureAwait(false);

                if (shouldRetry)
                {
                    context.Logger.LogDebug("🔄 Received 401 — invalidating credentials and retrying once~");
                    response.Dispose();
                    response = await SendThroughPipelineAsync(
                        client,
                        resiliencePipeline,
                        callState,
                        BuildRequest,
                        strategy,
                        context,
                        timeoutCts.Token).ConfigureAwait(false);
                }
            }

            sw.Stop();

            // 5️⃣ Read + decode response body (Phase 2.3.1 — delegates to ResponseBodyDecoder)~ 📥
            var responseBytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            object? decodedBody = ResponseBodyDecoder.Decode(responseBytes, contentType);

            var outputs = new Dictionary<string, object?>
            {
                ["statusCode"] = (int)response.StatusCode,
                ["headers"] = FlattenHeaders(response),
                ["body"] = decodedBody,
                ["success"] = response.IsSuccessStatusCode,
                ["durationMs"] = sw.ElapsedMilliseconds,
                ["contentType"] = contentType,
                ["attemptCount"] = callState.AttemptCount,
                ["circuitState"] = callState.CircuitState,
            };

            return ModuleResult.Ok(outputs);
        }
        catch (InvalidOperationException ioe) when (ioe.Message.StartsWith("Body encode failed:", StringComparison.Ordinal))
        {
            sw.Stop();
            return ModuleResult.Fail(ioe.Message);
        }
        catch (OAuth2AuthorizationException oae)
        {
            sw.Stop();
            return ModuleResult.Fail(
                $"OAuth2 token fetch failed [{oae.ErrorCode}]: {oae.Description ?? oae.Message}~ 💔",
                oae);
        }
        catch (BrokenCircuitException bce)
        {
            sw.Stop();
            return ModuleResult.Fail($"HTTP request blocked by open circuit breaker — {methodStr} {uri}~ 🛑", bce);
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return ModuleResult.Fail(
                $"HTTP request timed out after {timeoutSeconds}s — {methodStr} {uri}~ 💔",
                oce);
        }
        catch (OperationCanceledException oce)
        {
            sw.Stop();
            return ModuleResult.Fail($"HTTP request cancelled — {methodStr} {uri}~ 🛑", oce);
        }
        catch (HttpRequestException hre)
        {
            sw.Stop();
            return ModuleResult.Fail($"HTTP request failed: {hre.Message}~ 💔", hre);
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// 🔄 Phase 2.3.4 — Run the request through the Polly resilience pipeline.
    /// Each attempt rebuilds the <see cref="HttpRequestMessage"/> (they aren't resendable) and
    /// re-applies the auth strategy so refreshed OAuth2 tokens propagate transparently~
    /// </summary>
    private static async Task<HttpResponseMessage> SendThroughPipelineAsync(
        HttpClient client,
        ResiliencePipeline<HttpResponseMessage> pipeline,
        ResilienceCallState callState,
        Func<HttpRequestMessage> buildRequest,
        IHttpAuthStrategy strategy,
        ModuleExecutionContext context,
        CancellationToken cancellationToken)
    {
        var pollyContext = ResilienceContextPool.Shared.Get(cancellationToken);
        pollyContext.Properties.Set(HttpResiliencePipelineFactory.StateKey, callState);

        try
        {
            return await pipeline.ExecuteAsync(
                static async (ctx, state) =>
                {
                    var (client, buildRequest, strategy, moduleCtx) = state;
                    var attemptRequest = buildRequest();
                    try
                    {
                        await strategy.ApplyAsync(attemptRequest, moduleCtx, ctx.CancellationToken).ConfigureAwait(false);
                        LogRedactedHeaders(moduleCtx.Logger, attemptRequest);
                        return await client.SendAsync(attemptRequest, HttpCompletionOption.ResponseHeadersRead, ctx.CancellationToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        attemptRequest.Dispose();
                    }
                },
                pollyContext,
                (client, buildRequest, strategy, context)).ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(pollyContext);
        }
    }

    /// <summary>Emit a debug log line with redacted headers~ 🔒.</summary>
    private static void LogRedactedHeaders(ILogger logger, HttpRequestMessage request)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var snap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers)
        {
            snap[h.Key] = HttpAuthStrategyFactory.RedactForLog(h.Key, string.Join(", ", h.Value));
        }

        logger.LogDebug(
            "🔐 Request headers (redacted): {Headers}",
            string.Join(", ", snap.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    /// <summary>Returns true for HTTP methods that conventionally carry no body~ 🚫.</summary>
    private static bool IsMethodWithoutBody(string method)
        => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true if a header name belongs on <see cref="HttpContent"/> not <see cref="HttpRequestMessage"/>~ 🏷️.</summary>
    private static bool IsContentHeader(string name)
        => name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Expires", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Last-Modified", StringComparison.OrdinalIgnoreCase);


    /// <summary>Flatten an <see cref="HttpResponseMessage"/>'s response + content headers into a plain dict~ 🏷️.</summary>
    private static Dictionary<string, string> FlattenHeaders(HttpResponseMessage response)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
        {
            dict[h.Key] = string.Join(", ", h.Value);
        }

        if (response.Content is not null)
        {
            foreach (var h in response.Content.Headers)
            {
                dict[h.Key] = string.Join(", ", h.Value);
            }
        }

        return dict;
    }

    /// <summary>Extract a header dictionary from properties — supports <see cref="IDictionary{TKey, TValue}"/> shapes~ 🔧.</summary>
    private static Dictionary<string, string>? ExtractHeaderMap(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        switch (raw)
        {
            case IDictionary<string, string> sd:
                foreach (var kv in sd)
                {
                    result[kv.Key] = kv.Value;
                }

                break;
            case IDictionary<string, object?> od:
                foreach (var kv in od)
                {
                    result[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                }

                break;
            case System.Collections.IDictionary nd:
                foreach (System.Collections.DictionaryEntry entry in nd)
                {
                    result[entry.Key?.ToString() ?? string.Empty] = entry.Value?.ToString() ?? string.Empty;
                }

                break;
            default:
                return null;
        }

        return result;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out var v) && v is not null ? v.ToString() : null;

    private static int? TryParseInt(IReadOnlyDictionary<string, object?> props, string key)
    {
        if (!props.TryGetValue(key, out var v) || v is null)
        {
            return null;
        }

        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}






