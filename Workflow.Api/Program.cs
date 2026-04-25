using System.Security.Claims;
using Workflow.Engine.Services;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Composite;
using Workflow.Persistence.Nats;
using Workflow.Persistence.Postgres;
using Workflow.Persistence.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IExecutionStateStore, InMemoryExecutionStateStore>();

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
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

if (persistenceProvider is not null)
{
    await persistenceProvider.InitializeAsync().ConfigureAwait(false);
}

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
