using Akka.Actor;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Workflow.Api.Auth;
using Workflow.Api.Database;
using Workflow.Api.Observability;
using Workflow.Api.V1;
using Workflow.Api.Webhooks;
using Workflow.Core.Abstractions;
using Workflow.Engine.Actors;
using Workflow.Engine.Services;
using Workflow.Modules;
using Workflow.Modules.Abstractions;
using Workflow.Modules.Builtin;
using Workflow.Modules.Builtin.Http;
using Workflow.Modules.Database;
using Workflow.Modules.Database.Abstractions;
using Workflow.Modules.Database.Configuration;
using Workflow.Modules.Database.Linq;
using Workflow.Modules.Cloud;
using Workflow.Modules.Cloud.Configuration;
using Workflow.Modules.Transform.Script;
using Workflow.Scripting;
using Workflow.Scripting.Roslyn;
using Workflow.Scripting.Lua;
using Workflow.Api.Transform;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Composite;
using Workflow.Persistence.Nats;
using Workflow.Persistence.Postgres;
using Workflow.Persistence.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkflowSwagger();

// 🚨 Phase 2.7.0 — RFC 7807 ProblemDetails for consistent error responses (D8)~
builder.Services.AddProblemDetails();

// 🔤 Phase 2.7.0 — Teach the Minimal-API JSON pipeline the LanguageExt converters so
// WorkflowDefinition (Arr<>/HashMap<>) binds + serializes over HTTP the same way persistence does~
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new Workflow.Persistence.Sqlite.Serialization.ArrJsonConverterFactory());
    o.SerializerOptions.Converters.Add(new Workflow.Persistence.Sqlite.Serialization.HashMapStringJsonConverterFactory());
});

// 🛡️ Phase 2.7.1 — Module-aware workflow validator (validates POST/PUT definitions → 422)~
builder.Services.AddSingleton<Workflow.Modules.Validation.ModuleAwareWorkflowValidator>(sp =>
    new Workflow.Modules.Validation.ModuleAwareWorkflowValidator(sp.GetRequiredService<IModuleRegistry>()));

// 🧩 Phase 2.7.2 — The engine's WorkflowSupervisor resolves the base validator from DI when it
// creates instances, so the API host must register it too~
builder.Services.AddSingleton<Workflow.Core.Abstractions.WorkflowValidator>();

// 📈 Phase 2.7.5 — Execution metrics seam + health checks (persistence + actor-system liveness)~
builder.Services.AddSingleton<Workflow.Api.Observability.IWorkflowMetrics, Workflow.Api.Observability.InMemoryWorkflowMetrics>();
builder.Services.AddHealthChecks()
    .AddCheck<Workflow.Api.Observability.PersistenceHealthCheck>(
        "persistence", tags: new[] { Workflow.Api.V1.MonitoringEndpoints.ReadyTag })
    .AddCheck<Workflow.Api.Observability.ActorSystemHealthCheck>(
        "actor-system", tags: new[] { Workflow.Api.V1.MonitoringEndpoints.LiveTag });

// 📦 Phase 2.7.0 — Register the module registry (D5) — the one API-DI gap. Seed builtins + the
// host-wired families (database/cloud/transform-script modules resolved from DI as IWorkflowModule)~
builder.Services.AddSingleton<IModuleRegistry>(sp =>
{
    var registry = new InMemoryModuleRegistry(
        sp.GetService<Microsoft.Extensions.Logging.ILogger<InMemoryModuleRegistry>>());
    foreach (var module in BuiltinModules.GetAll())
    {
        registry.RegisterModule(module, allowOverwrite: true);
    }

    foreach (var module in sp.GetServices<IWorkflowModule>())
    {
        registry.RegisterModule(module, allowOverwrite: true);
    }

    return registry;
});

builder.Services.AddSingleton<IExecutionStateStore, InMemoryExecutionStateStore>();

// 📦 Phase 2.8 — Module packaging / install / versioning / hot-reload services~
builder.Services.Configure<Workflow.Modules.Packaging.ModulePackagingOptions>(
    builder.Configuration.GetSection(Workflow.Modules.Packaging.ModulePackagingOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var opts = new Workflow.Modules.Packaging.ModulePackagingOptions();
    builder.Configuration.GetSection(Workflow.Modules.Packaging.ModulePackagingOptions.SectionName).Bind(opts);

    // The engine's SemVer version drives the MinEngineVersion install gate (Q6)~
    opts.EngineVersion ??= typeof(Workflow.Engine.Actors.WorkflowSupervisor).Assembly.GetName().Version?.ToString();

    // Bind the security sub-section (RequireSigned + trusted tokens, D9)~
    builder.Configuration.GetSection("Modules:Security").Bind(opts);

    // Q1: default archival on when a persistence provider is configured (unless explicitly set)~
    if (builder.Configuration["Modules:ArchivePackages"] is null
        && !string.IsNullOrWhiteSpace(builder.Configuration["Persistence:Provider"]))
    {
        opts.ArchivePackages = true;
    }

    return opts;
});
builder.Services.AddSingleton<Workflow.Modules.Packaging.ModulePackageReader>();
builder.Services.AddSingleton<Workflow.Modules.Packaging.IModulePackageArchive>(sp =>
{
    var blob = sp.GetService<IBlobStore>();
    return blob is not null
        ? new Workflow.Api.Modules.BlobStoreModulePackageArchive(blob)
        : new Workflow.Api.Modules.NoOpModulePackageArchive();
});
builder.Services.AddSingleton<Workflow.Modules.Security.IAssemblyVerifier>(sp =>
{
    var opts = sp.GetRequiredService<Workflow.Modules.Packaging.ModulePackagingOptions>();
    return new Workflow.Modules.Security.StrongNameVerifier(opts.TrustedPublicKeyTokens);
});
builder.Services.AddSingleton<Workflow.Modules.Loading.IModuleLoader>(sp =>
    new Workflow.Modules.Loading.AssemblyModuleLoader(
        sp.GetRequiredService<IModuleRegistry>(),
        logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<Workflow.Modules.Loading.AssemblyModuleLoader>>()));
builder.Services.AddSingleton<Workflow.Modules.Packaging.ModulePackageInstaller>(sp =>
    new Workflow.Modules.Packaging.ModulePackageInstaller(
        sp.GetRequiredService<Workflow.Modules.Loading.IModuleLoader>(),
        sp.GetRequiredService<IModuleRegistry>(),
        sp.GetRequiredService<Workflow.Modules.Packaging.ModulePackagingOptions>(),
        sp.GetRequiredService<Workflow.Modules.Packaging.ModulePackageReader>(),
        sp.GetService<Workflow.Modules.Packaging.IModulePackageArchive>(),
        sp.GetService<Workflow.Modules.Security.IAssemblyVerifier>(),
        sp.GetService<Microsoft.Extensions.Logging.ILogger<Workflow.Modules.Packaging.ModulePackageInstaller>>()));
builder.Services.AddSingleton<Workflow.Modules.Loading.IActiveExecutionTracker, Workflow.Api.Modules.MetricsActiveExecutionTracker>();

// 🗂️ Phase 2.8.2 — Module state store (file default; repository when Modules:StateStore=repository + provider, Q2)~
builder.Services.AddSingleton<Workflow.Modules.State.IModuleStateStore>(sp =>
{
    var opts = sp.GetRequiredService<Workflow.Modules.Packaging.ModulePackagingOptions>();
    var path = Workflow.Modules.State.ModuleStateStoreFactory.DefaultStateFilePath(opts.PackagesPath);
    return Workflow.Modules.State.ModuleStateStoreFactory.Create(
        builder.Configuration["Modules:StateStore"],
        path,
        sp.GetService<Workflow.Modules.State.IModuleStatePersistence>(),
        sp.GetService<Microsoft.Extensions.Logging.ILogger<Workflow.Modules.State.FileModuleStateStore>>());
});

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

// ☁️ Cloud storage module family (Phase 2.5.b)~ — S3 + Azure Blob modules.
// CopilotNote: host-wired (not in AddWorkflowModules) — Workflow.Modules.Cloud references
// Workflow.Modules, so the reverse call would be circular; same rule as AddDatabaseModules (D4)~
builder.Services.AddCloudStorageModules();
builder.Services.Configure<CloudStorageOptions>(
    builder.Configuration.GetSection(CloudStorageOptions.SectionName));

// 🧬 Typed linq family (Phase 2.4.b)~ — opt-in Roslyn compile/preview/cache pipeline (D14).
// Quarantined in Workflow.Modules.Database.Linq; the host opts in here so raw-SQL-only hosts don't pay for it~
builder.Services.AddDatabaseLinqModules();
builder.Services.Configure<LinqEndpointsOptions>(
    builder.Configuration.GetSection(LinqEndpointsOptions.SectionName));

//  Akka actor system + WorkflowSupervisor (Phase 2.3.9)~
// CopilotNote: The factory runs lazily on first IWorkflowLauncher resolution so the full DI
// container (including IWorkflowRepository) is available at that point~
builder.Services.AddSingleton(sp => ActorSystem.Create("dotflow"));
builder.Services.AddSingleton(sp =>
{
    var system = sp.GetRequiredService<ActorSystem>();
    // CopilotNote: WorkflowSupervisor.Props(sp) captures the root IServiceProvider — safe for
    // singleton actors that live for the process lifetime~
    var supervisorRef = system.ActorOf(WorkflowSupervisor.Props(sp), "workflow-supervisor");
    return new WorkflowSupervisorActorRef(supervisorRef);
});
builder.Services.AddSingleton<IWorkflowLauncher, ActorWorkflowLauncher>();  // 2.3.9 — replaces NullWorkflowLauncher~

// 🔄 Phase 2.8.3 — Hot-reload hosted service (self-disables unless Modules:HotReload:Enabled)~
builder.Services.AddHostedService<Workflow.Api.Modules.ModuleHotReloadHostedService>();

var persistenceProvider = BuildPersistenceProvider(builder.Configuration);
if (persistenceProvider is not null)
{
    builder.Services.AddSingleton<IPersistenceProvider>(persistenceProvider);
    builder.Services.AddSingleton(persistenceProvider);

    // Repositories are resolved through the provider so late-init providers (like NATS) remain safe.
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().Workflows);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().ExecutionHistory);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<IPersistenceProvider>().Variables);

    // 🚀 Phase 2.7.2 — General execution service (start/status/cancel) — needs the repos above~
    builder.Services.AddSingleton<Workflow.Api.Execution.IWorkflowExecutionService, Workflow.Api.Execution.ActorWorkflowExecutionService>();

    // 🗄️ Phase 2.8.2 — When a provider with blob support is configured, expose a persistence-backed
    // module state seam so Modules:StateStore=repository is available (Q2)~
    builder.Services.AddSingleton<Workflow.Modules.State.IModuleStatePersistence>(sp =>
    {
        var blob = sp.GetService<IBlobStore>();
        return blob is not null
            ? new Workflow.Api.Modules.BlobStoreModuleStatePersistence(blob)
            : new Workflow.Api.Modules.NoOpModuleStatePersistence();
    });

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

// 🌟 Phase 2.6.b — Typed transform-script family (opt-in Roslyn; uses the IBlobStore above for the
// compiled-assembly cache). Host-wired, NOT in AddWorkflowModules() (Roslyn quarantine, D4)~
builder.Services.AddTransformScriptModules();

// 📜 Phase 3.1 — General-purpose scripting: JS (Jint) + C# (Roslyn) + Lua (MoonSharp) executors for
// builtin.script + the /api/v1/scripts endpoints. Host ceilings bound from Scripting:*~
builder.Services.AddSingleton(sp =>
{
    var ceilings = new Workflow.Scripting.Abstractions.ScriptHostCeilings();
    builder.Configuration.GetSection(Workflow.Scripting.Abstractions.ScriptHostCeilings.SectionName).Bind(ceilings);
    return ceilings;
});
builder.Services.AddWorkflowScripting();
builder.Services.AddRoslynScripting();
builder.Services.AddLuaScripting();
builder.Services.AddSingleton<Workflow.Scripting.Libraries.IScriptLibraryStore>(sp =>
{
    var blob = sp.GetService<IBlobStore>();
    return blob is not null
        ? new Workflow.Scripting.Libraries.PersistedScriptLibraryStore(new Workflow.Api.Modules.BlobScriptLibraryPersistence(blob))
        : new Workflow.Scripting.Libraries.InMemoryScriptLibraryStore();
});

// 🔐 Phase 2.7.7 — API authentication (API-key + JWT bearer) + named authorization policies~
builder.Services.AddWorkflowApiAuth(builder.Configuration);

// 🚦 Phase 2.7.8 — Rate-limiting seam (disabled unless Api:RateLimit:Enabled)~
builder.Services.AddWorkflowRateLimiting(builder.Configuration);

var app = builder.Build();

// 📖 Phase 2.7.8 — Serve the OpenAPI document + UI (docs are useful in every environment)~
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// 🔐 Phase 2.7.7 — Authenticate then authorize (no-op policies when Api:Auth:Require=false)~
app.UseAuthentication();
app.UseAuthorization();

// 🚦 Phase 2.7.8 — Rate-limiting seam (no-op unless Api:RateLimit:Enabled)~
app.UseRateLimiter();

//  Phase 2.3.6 — Webhook trigger + management endpoints~
app.MapWebhookEndpoints();

// 📇 Phase 2.4.a.5 — Named database-connection CRUD endpoints~
app.MapDatabaseConnectionEndpoints();

// 🧬 Phase 2.4.b.5 — Typed linq validate/preview/compile + catalog import endpoints~
app.MapDatabaseLinqEndpoints();

// 🌟 Phase 2.6.b.2 — Typed transform-script validate/preview/compile endpoints~
app.MapTransformScriptEndpoints();

// 📋 Phase 2.7.1 — Workflow definition CRUD endpoints (/api/v1/workflows)~
app.MapWorkflowEndpoints();

// ⚡ Phase 2.7.2 — Execution endpoints (start/by-name/sync/status/cancel/list)~
app.MapExecutionEndpoints();

// 📦 Phase 2.7.3 — Module discovery endpoints (/api/v1/modules)~
app.MapModuleEndpoints();

// 📦 Phase 2.8.5 — Module management endpoints (upload/enable/disable/uninstall)~
app.MapModuleManagementEndpoints();

// 🔧 Phase 2.7.4 — Variable endpoints (/api/v1/variables)~
app.MapVariableEndpoints();

// 📜 Phase 3.1.6 — Script test + language + library endpoints (/api/v1/scripts)~
app.MapScriptEndpoints();

// 📊 Phase 2.7.5 — Monitoring endpoints (/api/v1/health, /status, /metrics)~
app.MapMonitoringEndpoints();

if (persistenceProvider is not null)
{
    await persistenceProvider.InitializeAsync().ConfigureAwait(false);
}

// 📇 Phase 2.4.a.5 — Seed config-declared connections into the active registry (so the
// SQLite-persisted registry still honours appsettings Workflow:Database:Connections). Idempotent:
// only inserts ids that aren't already present~
await SeedConfiguredConnectionsAsync(app.Services).ConfigureAwait(false);

// 🔁 Phase 2.8 — Rehydrate previously-installed module packages + apply persisted enabled state~
await RehydrateModulesAsync(app.Services).ConfigureAwait(false);

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (HttpContext httpContext) =>
    {
        var callerId = httpContext.ResolveCallerId();
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

// 🔁 Phase 2.8 — Reload installed packages on start and apply persisted enabled/disabled state~
static async Task RehydrateModulesAsync(IServiceProvider services)
{
    var installer = services.GetService<Workflow.Modules.Packaging.ModulePackageInstaller>();
    if (installer is not null)
    {
        try
        {
            await installer.RehydrateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?
                .CreateLogger("ModuleRehydration")
                .LogWarning(ex, "🔁 Module rehydration failed~");
        }
    }

    var stateStore = services.GetService<Workflow.Modules.State.IModuleStateStore>();
    var moduleRegistry = services.GetService<IModuleRegistry>();
    if (stateStore is not null && moduleRegistry is not null)
    {
        try
        {
            var snapshot = await stateStore.LoadAsync().ConfigureAwait(false);
            foreach (var record in snapshot.Modules)
            {
                if (Version.TryParse(record.Version, out var version))
                {
                    moduleRegistry.SetModuleEnabled(record.ModuleId, version, record.Enabled);
                }
            }
        }
        catch (Exception ex)
        {
            services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?
                .CreateLogger("ModuleState")
                .LogWarning(ex, "🗂️ Applying module state failed~");
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

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

internal sealed record ProviderConfig(string Provider, string ConnectionString);
