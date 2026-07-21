// <copyright file="DatabaseConnectionEndpoints.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Database;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;

/// <summary>
/// 📇 Phase 2.4.a.5 — Minimal-API endpoints for named database-connection CRUD~ ✨💖.
/// </summary>
/// <remarks>
/// <para>
/// Routes (all under <c>/api/database/connections</c>):
/// <list type="bullet">
///   <item><description><c>GET /</c> — list (connection strings masked)</description></item>
///   <item><description><c>GET /{id}</c> — get one (masked; <c>?reveal=true</c> returns plaintext)</description></item>
///   <item><description><c>POST /</c> — upsert (403 when <c>DisableRuntimeCrud</c>)</description></item>
///   <item><description><c>DELETE /{id}</c> — delete (403 when <c>DisableRuntimeCrud</c>)</description></item>
/// </list>
/// </para>
/// <para>
/// CopilotNote: Connection strings are ALWAYS masked in responses unless <c>?reveal=true</c>.
/// An admin authorization policy for reveal is a TODO — no auth infrastructure exists in the API
/// yet (webhooks are also unauthenticated in V1), so reveal is currently ungated. Tracked for the
/// API-security pass~ 🔐.
/// </para>
/// </remarks>
public static class DatabaseConnectionEndpoints
{
    private const string Masked = "***";

    /// <summary>Registers the database-connection management endpoints~ 📇.</summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapDatabaseConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/database/connections").WithTags("Database Connections");

        group.MapGet("/", ListConnectionsHandler)
            .WithName("ListDatabaseConnections")
            .WithSummary("List named database connections (connection strings masked)")
            .Produces<DbConnectionResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/{id}", GetConnectionHandler)
            .WithName("GetDatabaseConnection")
            .WithSummary("Get a named database connection (masked unless ?reveal=true)")
            .Produces<DbConnectionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", UpsertConnectionHandler)
            .WithName("UpsertDatabaseConnection")
            .WithSummary("Create or update a named database connection")
            .Produces<DbConnectionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id}", DeleteConnectionHandler)
            .WithName("DeleteDatabaseConnection")
            .WithSummary("Delete a named database connection")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListConnectionsHandler(
        IDbConnectionRegistry registry,
        CancellationToken ct)
    {
        var all = await registry.ListAsync(ct).ConfigureAwait(false);
        var masked = all.Select(d => ToResponse(d, reveal: false)).ToArray();
        return Results.Ok(masked);
    }

    private static async Task<IResult> GetConnectionHandler(
        string id,
        bool? reveal,
        IDbConnectionRegistry registry,
        CancellationToken ct)
    {
        var found = await registry.GetAsync(id, ct).ConfigureAwait(false);
        return found.Match(
            Some: d => Results.Ok(ToResponse(d, reveal ?? false)),
            None: () => Results.NotFound(new { error = $"Connection '{id}' not found." }));
    }

    private static async Task<IResult> UpsertConnectionHandler(
        DbConnectionRequest req,
        IDbConnectionRegistry registry,
        IOptions<DatabaseConnectionsOptions> options,
        CancellationToken ct)
    {
        if (options.Value.DisableRuntimeCrud)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(req.Id))
        {
            return Results.BadRequest(new { error = "Id is required." });
        }

        if (string.IsNullOrWhiteSpace(req.ProviderKey))
        {
            return Results.BadRequest(new { error = "ProviderKey is required." });
        }

        if (string.IsNullOrWhiteSpace(req.ConnectionString))
        {
            return Results.BadRequest(new { error = "ConnectionString is required." });
        }

        var descriptor = new DbConnectionDescriptor(
            Id: req.Id,
            ProviderKey: req.ProviderKey,
            ConnectionString: req.ConnectionString,
            DisplayName: req.DisplayName,
            Enabled: req.Enabled ?? true);

        await registry.UpsertAsync(descriptor, ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(descriptor, reveal: false));
    }

    private static async Task<IResult> DeleteConnectionHandler(
        string id,
        IDbConnectionRegistry registry,
        IOptions<DatabaseConnectionsOptions> options,
        CancellationToken ct)
    {
        if (options.Value.DisableRuntimeCrud)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var deleted = await registry.DeleteAsync(id, ct).ConfigureAwait(false);
        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = $"Connection '{id}' not found." });
    }

    private static DbConnectionResponse ToResponse(DbConnectionDescriptor d, bool reveal) =>
        new(
            Id: d.Id,
            ProviderKey: d.ProviderKey,
            ConnectionString: reveal ? d.ConnectionString : Masked,
            DisplayName: d.DisplayName,
            Enabled: d.Enabled);
}

/// <summary>Request body for creating/updating a named database connection~ 📋.</summary>
/// <param name="Id">Unique connection id (case-insensitive).</param>
/// <param name="ProviderKey">Provider key ("postgres"/"sqlite").</param>
/// <param name="ConnectionString">The connection string (encrypted at rest when persisted).</param>
/// <param name="DisplayName">Optional friendly name.</param>
/// <param name="Enabled">Whether the connection is usable (default true).</param>
public sealed record DbConnectionRequest(
    string Id,
    string ProviderKey,
    string ConnectionString,
    string? DisplayName = null,
    bool? Enabled = null);

/// <summary>Response DTO for a named database connection (connection string masked by default)~ 📤.</summary>
/// <param name="Id">Connection id.</param>
/// <param name="ProviderKey">Provider key.</param>
/// <param name="ConnectionString">Masked (<c>***</c>) unless revealed.</param>
/// <param name="DisplayName">Optional friendly name.</param>
/// <param name="Enabled">Whether the connection is usable.</param>
public sealed record DbConnectionResponse(
    string Id,
    string ProviderKey,
    string ConnectionString,
    string? DisplayName,
    bool Enabled);

