// <copyright file="ScriptEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts.Scripts;
using Workflow.Modules.Internal;
using Workflow.Scripting.Abstractions;
using Workflow.Scripting.Api;
using Workflow.Scripting.Libraries;

/// <summary>
/// 🧪🌐 Phase 3.1.6 — Script test + language discovery + library management endpoints under
/// <c>/api/v1/scripts</c>, on the 2.7 Minimal-API conventions (D10)~ ✨💖.
/// </summary>
public static class ScriptEndpoints
{
    /// <summary>Registers the <c>/api/v1/scripts</c> endpoints~ 🧪.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapScriptEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/scripts").WithTags("Scripts");

        group.MapPost("/test", TestHandler).WithName("TestScript").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapGet("/languages", LanguagesHandler).WithName("ListScriptLanguages").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/libraries", ListLibrariesHandler).WithName("ListScriptLibraries").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/libraries/{libraryId}", GetLibraryHandler).WithName("GetScriptLibrary").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapPut("/libraries/{libraryId}", PutLibraryHandler).WithName("PutScriptLibrary").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapDelete("/libraries/{libraryId}", DeleteLibraryHandler).WithName("DeleteScriptLibrary").RequireAuthorization(AuthConstants.WorkflowWritePolicy);

        return app;
    }

    private static async Task<IResult> TestHandler(HttpContext http, ScriptTestRequest request, CancellationToken ct)
    {
        var factory = http.RequestServices.GetService<IScriptExecutorFactory>();
        if (factory is null)
        {
            return ApiResults.ServiceUnavailableProblem("Scripting is not configured.");
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Code))
        {
            return ApiResults.Problem422("Script code is required.");
        }

        var executor = factory.GetExecutor(request.Language);
        if (executor is null)
        {
            var available = string.Join(", ", factory.GetRegisteredLanguages().Select(l => l.LanguageId));
            return ApiResults.Problem422($"Unknown script language '{request.Language}'. Registered: {available}.");
        }

        // Resolve libraries (dependency-ordered)~
        IReadOnlyList<ScriptLibrarySource> libraries = Array.Empty<ScriptLibrarySource>();
        if (request.Libraries is { Count: > 0 })
        {
            var store = http.RequestServices.GetService<IScriptLibraryStore>();
            if (store is null)
            {
                return ApiResults.ServiceUnavailableProblem("Script libraries are not configured.");
            }

            try
            {
                libraries = await store.ResolveAsync(request.Language, request.Libraries, ct).ConfigureAwait(false);
            }
            catch (ScriptLibraryException ex)
            {
                return ApiResults.Problem422(ex.Message);
            }
        }

        var ceilings = http.RequestServices.GetService<ScriptHostCeilings>() ?? ScriptHostCeilings.Default;
        var config = BuildConfig(request.Config).ClampTo(ceilings);

        var inputs = NormalizeInputs(request.Inputs);
        var api = new WorkflowScriptApi(new WorkflowScriptApiOptions
        {
            Variables = new Dictionary<string, object?>(),
            Config = config,
            NodeId = "test",
            HttpClientFactory = http.RequestServices.GetService<IHttpClientFactory>(),
            CancellationToken = ct,
        });

        var context = new ScriptExecutionContext
        {
            Inputs = inputs,
            Variables = new Dictionary<string, object?>(),
            Api = api,
            Config = config,
            NodeId = "test",
            Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            Libraries = libraries,
        };

        var result = await executor.ExecuteAsync(request.Code, context, ct).ConfigureAwait(false);

        var dto = new ScriptTestResultDto(
            result.Success,
            result.ReturnValue,
            result.Logs.Select(ScriptLogEntryDto.From).ToList(),
            result.VariableUpdates,
            result.Duration.TotalMilliseconds,
            result.Error);

        // Script errors are returned as a 200 with success=false (D10) so callers see logs/duration~
        return Results.Ok(dto);
    }

    private static IResult LanguagesHandler(HttpContext http)
    {
        var factory = http.RequestServices.GetService<IScriptExecutorFactory>();
        if (factory is null)
        {
            return ApiResults.ServiceUnavailableProblem("Scripting is not configured.");
        }

        return Results.Ok(factory.GetRegisteredLanguages());
    }

    private static async Task<IResult> ListLibrariesHandler(HttpContext http, string? language, CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IScriptLibraryStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("Script libraries are not configured.");
        }

        var libs = await store.GetAllAsync(language, ct).ConfigureAwait(false);
        return Results.Ok(libs.Select(ScriptLibraryDto.From).ToList());
    }

    private static async Task<IResult> GetLibraryHandler(HttpContext http, string libraryId, CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IScriptLibraryStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("Script libraries are not configured.");
        }

        var lib = await store.GetAsync(libraryId, ct).ConfigureAwait(false);
        return lib is null ? ApiResults.NotFoundProblem($"Library '{libraryId}' was not found.") : Results.Ok(ScriptLibraryDto.From(lib));
    }

    private static async Task<IResult> PutLibraryHandler(HttpContext http, string libraryId, ScriptLibraryDto library, CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IScriptLibraryStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("Script libraries are not configured.");
        }

        var definition = (library with { }).ToDefinition() with { LibraryId = libraryId };
        try
        {
            await store.SaveAsync(definition, ct).ConfigureAwait(false);
        }
        catch (ScriptLibraryException ex)
        {
            return ApiResults.Problem422(ex.Message);
        }

        return Results.Ok(ScriptLibraryDto.From(definition));
    }

    private static async Task<IResult> DeleteLibraryHandler(HttpContext http, string libraryId, CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IScriptLibraryStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("Script libraries are not configured.");
        }

        var removed = await store.DeleteAsync(libraryId, ct).ConfigureAwait(false);
        return removed ? Results.NoContent() : ApiResults.NotFoundProblem($"Library '{libraryId}' was not found.");
    }

    private static ScriptExecutionConfig BuildConfig(ScriptTestConfig? config)
        => new()
        {
            TimeoutSeconds = config?.TimeoutSeconds ?? 30,
            AllowNetwork = config?.AllowNetwork ?? false,
            AllowFileSystem = config?.AllowFileSystem ?? false,
            AllowedPaths = config?.AllowedPaths ?? Array.Empty<string>(),
        };

    private static IReadOnlyDictionary<string, object?> NormalizeInputs(IReadOnlyDictionary<string, object?>? inputs)
    {
        var result = new Dictionary<string, object?>();
        if (inputs is null)
        {
            return result;
        }

        foreach (var (key, value) in inputs)
        {
            result[key] = value is System.Text.Json.JsonElement je ? JsonValueConverter.FromElement(je) : value;
        }

        return result;
    }
}
