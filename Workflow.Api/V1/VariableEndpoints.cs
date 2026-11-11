// <copyright file="VariableEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts;
using Workflow.Api.Variables;
using Workflow.Modules.Internal;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;

/// <summary>
/// 🔧 Phase 2.7.4 — Scoped, versioned variable management over <see cref="IVariableStore"/>~ ✨💖.
/// </summary>
public static class VariableEndpoints
{
    /// <summary>Registers the <c>/api/v1/variables</c> endpoints~ 🔧.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapVariableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapV1Group().MapGroup("/variables").WithTags("Variables");

        group.MapGet("/", ListHandler).WithName("ListVariables").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{name}", GetHandler).WithName("GetVariable").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapGet("/{name}/history", HistoryHandler).WithName("GetVariableHistory").RequireAuthorization(AuthConstants.WorkflowReadPolicy);
        group.MapPut("/{name}", SetHandler).WithName("SetVariable").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapDelete("/{name}", DeleteHandler).WithName("DeleteVariable").RequireAuthorization(AuthConstants.WorkflowWritePolicy);

        // 🔒 Plan 08 — secret variables: change the mode (purging earlier values needs confirmPurge),
        // and preview which stored variables a workflow save would turn secret (designer dialog)~
        group.MapPut("/{name}/secret", SecretModeHandler).WithName("SetVariableSecretMode").RequireAuthorization(AuthConstants.WorkflowWritePolicy);
        group.MapPost("/secret-impact", SecretImpactHandler).WithName("PreviewSecretImpact").RequireAuthorization(AuthConstants.WorkflowReadPolicy);

        return app;
    }

    /// <summary>
    /// 🔐 Phase 3.5 (V9.1) — global-scope writes additionally require <see cref="AuthConstants.AdminPolicy"/>.
    /// </summary>
    /// <remarks>
    /// The route-level policy is <c>WorkflowWrite</c>, which Developers hold — fine for
    /// workflow/execution scope, but a global variable is environment configuration with blast
    /// radius across every workflow. The check is done in-handler because the scope arrives as a
    /// query parameter, and it goes through <see cref="IAuthorizationService"/> rather than a raw
    /// role test so it honours the same <c>Api:Auth:Require</c> escape hatch as every other policy.
    /// <para>
    /// This is a deliberate breaking change (plan Q14): Developer-token automation that manages
    /// globals will now get a 403.
    /// </para>
    /// </remarks>
    private static async Task<IResult?> RequireAdminForGlobalScope(HttpContext http, VariableScope scope)
    {
        if (scope.Kind != VariableScopeKind.Global)
        {
            return null;
        }

        var authorization = http.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(http.User, AuthConstants.AdminPolicy).ConfigureAwait(false);

        return result.Succeeded
            ? null
            : ApiResults.ForbiddenProblem(
                "Global variables are shared by every workflow, so changing them requires the Admin role.");
    }

    private static async Task<IResult> ListHandler(
        HttpContext http,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var all = await store.GetAllVariablesAsync(variableScope, ct).ConfigureAwait(false);

        // 🔒 Plan 08 — secret values are write-only~
        var masked = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in all)
        {
            masked[key] = await IsSecretAsync(http, variableScope, key, ct).ConfigureAwait(false) ? VariableDto.Mask : value;
        }

        return Results.Ok(masked);
    }

    private static async Task<IResult> GetHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        int? version,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        var entry = await store.GetVariableAsync(variableScope, name, version, ct).ConfigureAwait(false);
        return entry is null
            ? ApiResults.NotFoundProblem($"Variable '{name}' was not found in {variableScope.Kind} scope.")
            : Results.Ok(await DescribeAsync(http, entry, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> HistoryHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        // 🔒 Plan 08 — a secret's history is who/when only (it keeps no earlier values)~
        if (await IsSecretAsync(http, variableScope, name, ct).ConfigureAwait(false)
            && http.RequestServices.GetService<SecretVariableService>()?.Get(variableScope, name) is { } state)
        {
            var current = await store.GetVariableAsync(variableScope, name, null, ct).ConfigureAwait(false);
            return Results.Ok(state.Audit.Select((a, i) => new VariableDto(
                name, null, current?.ValueTypeName ?? "secret", i + 1, variableScope.Kind.ToString(),
                variableScope.WorkflowId ?? variableScope.ExecutionId, a.At, a.At)
            {
                IsSecret = true,
                SecretMode = state.Mode.ToString(),
                LastSetBy = a.By,
                LastSetAt = a.At,
                Change = a.Action,
            }).ToList());
        }

        var history = await store.GetVariableHistoryAsync(variableScope, name, ct).ConfigureAwait(false);
        var dtos = new List<VariableDto>(history.Count);
        foreach (var entry in history)
        {
            dtos.Add(VariableDto.From(entry));
        }

        return Results.Ok(dtos);
    }

    private static async Task<IResult> SetHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        SetVariableRequest? request,
        bool? confirmPurge,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        if (await RequireAdminForGlobalScope(http, variableScope).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        SecretVariableStore.Caller = CallerOf(http);

        // 🔒 Plan 08 — a new variable can be created secret in one call~
        if (request?.SecretMode is { } requestedMode)
        {
            if (!Enum.TryParse<SecretVariableMode>(requestedMode, ignoreCase: true, out var mode))
            {
                return ApiResults.BadRequestProblem($"Unknown secret mode '{requestedMode}' (use None, Secret or FollowWorkflows).");
            }

            if (await ChangeModeAsync(http, variableScope, name, mode, request.Value, confirmPurge, writeValue: false, ct).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }
        }

        await store.SetVariableAsync(variableScope, name, request?.ToClrValue(), ct).ConfigureAwait(false);
        var entry = await store.GetVariableAsync(variableScope, name, null, ct).ConfigureAwait(false);
        return entry is null
            ? ApiResults.ServiceUnavailableProblem("The variable could not be read back after writing.")
            : Results.Ok(await DescribeAsync(http, entry, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> DeleteHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        CancellationToken ct)
    {
        var store = http.RequestServices.GetService<IVariableStore>();
        if (store is null)
        {
            return ApiResults.ServiceUnavailableProblem("No persistence provider is configured.");
        }

        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error))
        {
            return ApiResults.BadRequestProblem(error);
        }

        if (await RequireAdminForGlobalScope(http, variableScope).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        SecretVariableStore.Caller = CallerOf(http);
        var deleted = await store.DeleteVariableAsync(variableScope, name, ct).ConfigureAwait(false);
        return deleted
            ? Results.NoContent()
            : ApiResults.NotFoundProblem($"Variable '{name}' was not found in {variableScope.Kind} scope.");
    }

    /// <summary>🔒 Plan 08 — <c>PUT /{name}/secret</c>: change a variable's secret mode.</summary>
    private static async Task<IResult> SecretModeHandler(
        HttpContext http,
        string name,
        string? scope,
        Guid? scopeId,
        SecretModeRequest request,
        bool? confirmPurge,
        CancellationToken ct)
    {
        if (!TryBuildScope(scope, scopeId, out var variableScope, out var error) || variableScope.Kind == VariableScopeKind.Execution)
        {
            return ApiResults.BadRequestProblem(string.IsNullOrEmpty(error) ? "Secret modes apply to global and workflow variables." : error);
        }

        if (await RequireAdminForGlobalScope(http, variableScope).ConfigureAwait(false) is { } forbidden)
        {
            return forbidden;
        }

        if (!Enum.TryParse<SecretVariableMode>(request.Mode, ignoreCase: true, out var mode))
        {
            return ApiResults.BadRequestProblem($"Unknown secret mode '{request.Mode}' (use None, Secret or FollowWorkflows).");
        }

        SecretVariableStore.Caller = CallerOf(http);
        if (await ChangeModeAsync(http, variableScope, name, mode, request.Value, confirmPurge, writeValue: true, ct).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        var store = http.RequestServices.GetRequiredService<IVariableStore>();
        var entry = await store.GetVariableAsync(variableScope, name, null, ct).ConfigureAwait(false);
        return entry is null
            ? Results.Ok(new { name, secretMode = mode.ToString() })
            : Results.Ok(await DescribeAsync(http, entry, ct).ConfigureAwait(false));
    }

    /// <summary>🔒 Plan 08 — <c>POST /secret-impact</c>: which stored variables saving this workflow would turn secret.</summary>
    private static IResult SecretImpactHandler(HttpContext http, SecretImpactRequest request)
    {
        var service = http.RequestServices.GetService<SecretVariableService>();
        if (service is null)
        {
            return Results.Ok(Array.Empty<object>());
        }

        var affected = new List<object>();
        foreach (var name in request.SecretNames ?? new List<string>())
        {
            foreach (var scope in new[] { VariableScope.Global, VariableScope.ForWorkflow(request.WorkflowId) })
            {
                if (service.Get(scope, name) is { Mode: SecretVariableMode.FollowWorkflows, Sticky: false })
                {
                    affected.Add(new { name, scope = scope.Kind.ToString(), scopeId = scope.WorkflowId });
                }
            }
        }

        return Results.Ok(affected);
    }

    /// <summary>
    /// 🔒 Plan 08 — applies a mode change. Turning a variable with stored values secret erases its
    /// earlier versions, so it's refused (409, with the count) unless <paramref name="confirmPurge"/>.
    /// Turning a secret into Not secret needs the value re-entered (the old one can't be read back).
    /// </summary>
    private static async Task<IResult?> ChangeModeAsync(
        HttpContext http,
        VariableScope scope,
        string name,
        SecretVariableMode mode,
        JsonElement? value,
        bool? confirmPurge,
        bool writeValue,
        CancellationToken ct)
    {
        var service = http.RequestServices.GetService<SecretVariableService>();
        if (service is null || http.RequestServices.GetService<IVariableStore>() is not SecretVariableStore store)
        {
            return ApiResults.ServiceUnavailableProblem("Secret variables aren't available (no persistence provider).");
        }

        var (wasSecret, _) = await service.ResolveAsync(scope, name, ct).ConfigureAwait(false);
        var by = CallerOf(http);
        var newValue = value is { } v && v.ValueKind != JsonValueKind.Null ? JsonValueConverter.FromElement(v) : null;

        if (mode == SecretVariableMode.None)
        {
            if (wasSecret && writeValue && newValue is null)
            {
                return ApiResults.BadRequestProblem(
                    $"'{name}' is secret, so its value can't be read back: give the value to store when switching it to Not secret.");
            }

            await service.SetModeAsync(scope, name, mode, by, ct).ConfigureAwait(false);
            if (wasSecret && writeValue)
            {
                await store.ReplaceWithPlainAsync(scope, name, newValue, ct).ConfigureAwait(false);
            }

            return null;
        }

        var secretNow = mode == SecretVariableMode.Secret
                        || wasSecret
                        || await service.DeclaredSecretAsync(scope, name, ct).ConfigureAwait(false);
        var versions = wasSecret ? 0 : await store.VersionCountAsync(scope, name, ct).ConfigureAwait(false);
        if (secretNow && !wasSecret && versions > 0 && confirmPurge != true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Confirm erasing earlier values",
                detail: $"Marking '{name}' secret stores it encrypted and erases its {versions} earlier value{(versions == 1 ? string.Empty : "s")}, "
                        + "which can't be undone. Repeat with confirmPurge=true to go ahead.",
                extensions: new Dictionary<string, object?> { ["versions"] = versions });
        }

        await service.SetModeAsync(scope, name, mode, by, ct).ConfigureAwait(false);
        if (secretNow && !wasSecret)
        {
            if (mode == SecretVariableMode.FollowWorkflows)
            {
                await service.ResolveAsync(scope, name, ct).ConfigureAwait(false);
            }

            await store.ConvertToSecretAsync(scope, name, by, ct).ConfigureAwait(false);
        }

        if (writeValue && newValue is not null)
        {
            await store.SetVariableAsync(scope, name, newValue, ct).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<bool> IsSecretAsync(HttpContext http, VariableScope scope, string name, CancellationToken ct)
        => scope.Kind != VariableScopeKind.Execution
           && http.RequestServices.GetService<SecretVariableService>() is { } service
           && (await service.ResolveAsync(scope, name, ct).ConfigureAwait(false)).IsSecret;

    private static async Task<VariableDto> DescribeAsync(HttpContext http, VariableEntry entry, CancellationToken ct)
    {
        var dto = VariableDto.From(entry);
        var state = http.RequestServices.GetService<SecretVariableService>()?.Get(entry.Scope, entry.Name);
        if (!await IsSecretAsync(http, entry.Scope, entry.Name, ct).ConfigureAwait(false))
        {
            return dto with { SecretMode = (state?.Mode ?? SecretVariableMode.None).ToString() };
        }

        var last = state?.Audit.LastOrDefault();
        return dto with
        {
            Value = null,
            IsSecret = true,
            SecretMode = (state?.Mode ?? SecretVariableMode.Secret).ToString(),
            LastSetBy = last?.By,
            LastSetAt = last?.At,
        };
    }

    private static string CallerOf(HttpContext http) => http.User.Identity?.Name ?? "anonymous";

    private static bool TryBuildScope(string? scope, Guid? scopeId, out VariableScope variableScope, out string error)
    {
        variableScope = VariableScope.Global;
        error = string.Empty;

        switch ((scope ?? "global").Trim().ToLowerInvariant())
        {
            case "global":
                variableScope = VariableScope.Global;
                return true;
            case "workflow":
                if (scopeId is null || scopeId == Guid.Empty)
                {
                    error = "A scopeId query parameter is required for workflow scope.";
                    return false;
                }

                variableScope = VariableScope.ForWorkflow(scopeId.Value);
                return true;
            case "execution":
                if (scopeId is null || scopeId == Guid.Empty)
                {
                    error = "A scopeId query parameter is required for execution scope.";
                    return false;
                }

                variableScope = VariableScope.ForExecution(scopeId.Value);
                return true;
            default:
                error = $"Unknown scope '{scope}'. Use global, workflow, or execution.";
                return false;
        }
    }
}
