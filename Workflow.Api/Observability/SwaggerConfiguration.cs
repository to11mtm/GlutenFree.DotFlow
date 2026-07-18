// <copyright file="SwaggerConfiguration.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Observability;

using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Workflow.Api.Auth;

/// <summary>
/// 📖 Phase 2.7.8 — Swagger/OpenAPI enrichment: a versioned <c>v1</c> document, XML comments, and
/// the API-key + Bearer security schemes so the "Authorize" button works~ ✨💖.
/// </summary>
public static class SwaggerConfiguration
{
    /// <summary>Adds the enriched Swagger generator~ 📖.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddWorkflowSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "GlutenFree.DotFlow API",
                Version = "v1",
                Description = "Workflow definition, execution, module, variable, and monitoring endpoints~ ✨",
            });

            var apiKeyScheme = new OpenApiSecurityScheme
            {
                Name = AuthConstants.ApiKeyHeader,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API key auth. Send your key in the X-API-Key header.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = AuthConstants.ApiKeyScheme },
            };
            options.AddSecurityDefinition(AuthConstants.ApiKeyScheme, apiKeyScheme);

            var bearerScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT bearer auth. Send 'Bearer {token}' in the Authorization header.",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            options.AddSecurityDefinition("Bearer", bearerScheme);

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [apiKeyScheme] = Array.Empty<string>(),
                [bearerScheme] = Array.Empty<string>(),
            });

            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}
