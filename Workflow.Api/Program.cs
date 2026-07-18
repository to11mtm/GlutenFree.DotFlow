using System.Security.Claims;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Workflow.Api.Database;
using Workflow.Api.Webhooks;
using Workflow.Core.Abstractions;
using Workflow.Engine.Actors;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Database;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Linq;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Composite;
using Workflow.Persistence.Nats;
using Workflow.Persistence.Postgres;
using Workflow.Persistence.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IExecutionStateStore, InMemoryExecutionStateStore>();

//  Expression Evaluators (Phase 2.2.5)~ — default: Jint (JS/ES2020); opt-in fallback: DynamicExpresso (C#)
builder.Services.AddSingleton<IExpressionEvaluator, JintExpressionEvaluator>();
builder.Services.AddKeyedSingleton<IExpressionEvaluator, DynamicExpressoEvaluator>("csharp");
builder.Services.AddSingleton<IExpressionEvaluatorFactory, KeyedExpressionEvaluatorFactory>();

//  HTTP built-in modules (Phase 2.3.0)~ — IHttpClientFactory named client "dotflow.http"
// (was: builder.Services.AddHttpModules(); — now aggregated under AddWorkflowModules)
builder.Services.AddWorkflowModules();

//  Webhook services (Phase 2.3.6/2.3.9)~ — registration repository + dispatcher + response strategy
// CopilotNote (2.3.9): IWebhookRegistrationRepository is registered here as InMemory default.
// If a persistence provider with Webhooks support (e.g. SQLite) is configured, it is swapped in
// AFTER persistenceProvider is built (see after BuildPersistenceProvider call below)~
builder.Services.AddSingleton<IWebhookRegistrationRepository, InMemoryWebhookRegistrationRepository>();
builder.Services.AddSingleton<IWebhookResponseStrategy, Async202ResponseStrategy>();
builder.Services.AddSingleton<WebhookDispatcher>();

// 🗄️ Database module family (Phase 2.4.a.5)~ — shared infra + 4 built-in modules.
// CopilotNote: AddDatabaseModules() can't be called from AddWorkflowModules() (circular ref —
// Workflow.Modules.Database references Workflow.Modules), so the host wires it here (D14)~
builder.Services.AddDatabaseModules();
builder.Services.Configure<DatabaseConnectionsOptions>(
    builder.Configuration.GetSection(DatabaseConnectionsOptions.SectionName));

// 🔒 Data-Protection-backed connection-string encryption (replaces the no-op default from
// AddDatabaseModules) so persisted connection strings are encrypted at rest~
builder.Services.AddDataProtection();
builder.Services.AddSingleton<IConnectionStringProtector, DataProtectionConnectionStringProtector>();

// 🧬 Typed linq family (Phase 2.4.b)~ — opt-in Roslyn compile/preview/cache pipeline (D14).
// Quarantined in Workflow.Modules.Database.Linq; the host opts in here so raw-SQL-only hosts don't pay for it~
builder.Services.AddDatabaseLinqModules();
builder.Services.Configure<LinqEndpointsOptions>(
    builder.Configuration.GetSection(LinqEndpointsOptions.SectionName));

//  Akka actor system + WorkflowSupervisor (Phase 2.3.9)~
// CopilotNote: The factory runs lazily on first IWorkflowLauncher resolution so the full DI
// container (including IWorkflowRepository) is available at that point~
builder.Services.AddSingleton(sp =>
{
    var system = ActorSystem.Create("dotflow");
    // CopilotNote: WorkflowSupervisor.Props(sp) captures the root IServiceProvider — safe for
    // singleton actors that live for the process lifetime~
    var supervisorRef = system.ActorOf(WorkflowSupervisor.Props(sp), "workflow-supervisor");
    return new WorkflowSupervisorActorRef(supervisorRef);
});
builder.Services.AddSingleton<IWorkflowLauncher, ActorWorkflowLauncher>();  // 2.3.9 — replaces NullWorkflowLauncher~

var persistenceProvider = BuildPersistenceProvider(builder.Configuration);
if (persistenceProvider is not null)
{
    builder.Services.AddSingleton<IPersistenceProvider>(persistenceProvider);
    builder.Services.AddSingleton(persistenceProvider);

    // Repositories are resolved through the provider so late-init providers (like NATS) remain safe.
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().Workflows);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().ExecutionHistory);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().Variables);

    if (persistenceProvider.Blobs is not null)
    {
        builder.Services.AddSingleton<IBlobStore>(sp => sp.GetRequiredService<IPersistenceProvider>().Blobs!);
    }

    //  Phase 2.3.9 — If the persistence provider supports webhook persistence, use it~
    // CopilotNote: Swap out the InMemory default registered above with the provider's repo.
    // This keeps the DI registration order clean and avoids requiring providers to register themselves~
    if (persistenceProvider.Webhooks is not null)
    {
        builder.Services.AddSingleton<IWebhookRegistrationRepository>(persistenceProvider.Webhooks);
    }

    // 📇 Phase 2.4.a.5 — When SQLite persistence is configured, override the in-memory
    // connection registry with the SQLite-persisted one (encrypted at rest). The registry is
    // resolved lazily so the DataProtection-backed protector is available; the db_connections
    // table is created by Migration_006 during InitializeAsync~
    if (persistenceProvider is SqlitePersistenceProvider sqliteProvider)
    {
        builder.Services.AddSingleton<IDbConnectionRegistry>(sp =>
            sqliteProvider.CreateDbConnectionRegistry(sp.GetRequiredService<IConnectionStringProtector>()));
    }
}

// 🗃️ Phase 2.4.b.5 — Fallback blob store for the compiled-assembly cache when no persistence
// provider supplies one (dev/tests). A persistence-backed IBlobStore (registered above) wins via TryAdd~
builder.Services.TryAddSingleton<IBlobStore, InMemoryBlobStore>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

//  Phase 2.3.6 — Webhook trigger + management endpoints~
app.MapWebhookEndpoints();

// 📇 Phase 2.4.a.5 — Named database-connection CRUD endpoints~
app.MapDatabaseConnectionEndpoints();

// 🧬 Phase 2.4.b.5 — Typed linq validate/preview/compile + catalog import endpoints~
app.MapDatabaseLinqEndpoints();

if (persistenceProvider is not null)
{
    await persistenceProvider.InitializeAsync().ConfigureAwait(false);
}

// 📇 Phase 2.4.a.5 — Seed config-declared connections into the active registry (so the
// SQLite-persisted registry still honours appsettings Workflow:Database:Connections). Idempotent:
// only inserts ids that aren't already present~
await SeedConfiguredConnectionsAsync(app.Services).ConfigureAwait(false);

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (HttpContext httpContext) =>
    {
        var callerId = ResolveCallerId(httpContext);
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]))
            .ToArray();

        return Results.Ok(new
        {
            CallerId = callerId,
            Forecast = forecast,
        });
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.Run();

static IPersistenceProvider? BuildPersistenceProvider(IConfiguration configuration)
{
    var providerName = configuration["Persistence:Provider"];
    if (string.IsNullOrWhiteSpace(providerName))
    {
        return null;
    }

    return providerName.Trim().ToLowerInvariant() switch
    {
        "sqlite" => new SqlitePersistenceProvider(NormalizeSqliteConnectionString(configuration["Persistence:ConnectionString"])),
        "postgres" => new PostgresPersistenceProvider(configuration["Persistence:ConnectionString"]
            ?? throw new InvalidOperationException("Persistence:ConnectionString is required for postgres provider.")),
        "nats" => new NatsPersistenceProvider(configuration["Persistence:ConnectionString"]
            ?? throw new InvalidOperationException("Persistence:ConnectionString is required for nats provider.")),
        "composite" => BuildCompositeProvider(configuration),
        _ => throw new InvalidOperationException($"Unsupported persistence provider '{providerName}'."),
    };
}

// 📇 Phase 2.4.a.5 — Seeds appsettings-declared connections into the active IDbConnectionRegistry.
// No-op for the in-memory registry (it hydrates from config at construction); meaningful for the
// SQLite-persisted registry which starts empty. Idempotent — only inserts absent ids~
static async Task SeedConfiguredConnectionsAsync(IServiceProvider services)
{
    var options = services.GetRequiredService<IOptions<DatabaseConnectionsOptions>>().Value;
    if (options.Connections.Count == 0)
    {
        return;
    }

    var registry = services.GetRequiredService<IDbConnectionRegistry>();
    foreach (var (key, descriptor) in options.Connections)
    {
        var existing = await registry.GetAsync(key).ConfigureAwait(false);
        if (existing.IsNone)
        {
            await registry.UpsertAsync(descriptor with { Id = key }).ConfigureAwait(false);
        }
    }
}

static IPersistenceProvider BuildCompositeProvider(IConfiguration configuration)
{
    var workflows = ReadProviderSection(configuration.GetSection("Persistence:Composite:Workflows"));
    if (workflows is null)
    {
        throw new InvalidOperationException("Persistence:Composite:Workflows is required when using composite provider.");
    }

    var execution = ReadProviderSection(configuration.GetSection("Persistence:Composite:ExecutionHistory")) ?? workflows;
    var variables = ReadProviderSection(configuration.GetSection("Persistence:Composite:Variables")) ?? workflows;
    var blobs = ReadProviderSection(configuration.GetSection("Persistence:Composite:Blobs"));

    var workflowsProvider = CreateProvider(workflows);
    var executionProvider = CreateProvider(execution);
    var variablesProvider = CreateProvider(variables);
    var blobsProvider = blobs is null ? null : CreateProvider(blobs);

    return new CompositePersistenceProvider(workflowsProvider, executionProvider, variablesProvider, blobsProvider);
}

static ProviderConfig? ReadProviderSection(IConfiguration section)
{
    var provider = section["Provider"];
    if (string.IsNullOrWhiteSpace(provider))
    {
        return null;
    }

    return new ProviderConfig(provider.Trim(), section["ConnectionString"] ?? string.Empty);
}

static IPersistenceProvider CreateProvider(ProviderConfig config)
{
    return config.Provider.ToLowerInvariant() switch
    {
        "sqlite" => new SqlitePersistenceProvider(NormalizeSqliteConnectionString(config.ConnectionString)),
        "postgres" => new PostgresPersistenceProvider(config.ConnectionString),
        "nats" => new NatsPersistenceProvider(config.ConnectionString),
        _ => throw new InvalidOperationException($"Unsupported composite provider '{config.Provider}'."),
    };
}

static string NormalizeSqliteConnectionString(string? connectionString)
{
    if (string.Equals(connectionString, ":memory:", StringComparison.OrdinalIgnoreCase))
    {
        return "Data Source=:memory:;Cache=Shared;Mode=Memory";
    }

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Persistence:ConnectionString is required for sqlite provider.");
    }

    return connectionString;
}

static string ResolveCallerId(HttpContext context)
{
    if (context.Request.Headers.TryGetValue("X-Caller-Id", out var headerValue)
        && !string.IsNullOrWhiteSpace(headerValue))
    {
        return headerValue.ToString();
    }

    var user = context.User;
    if (user?.Identity?.IsAuthenticated == true)
    {
        var claim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(claim))
        {
            return claim;
        }
    }

    return "system";
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

internal sealed record ProviderConfig(string Provider, string ConnectionString);
