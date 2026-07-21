// <copyright file="AuthServiceCollectionExtensions.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Auth;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// 🔐 Phase 2.7.7 — Registers the API-key + JWT bearer schemes and the named authorization
/// policies. When <c>Api:Auth:Require</c> is <c>false</c> (dev default) every policy is a no-op so
/// endpoints stay anonymous-friendly~ ✨💖.
/// </summary>
/// <remarks>
/// All config-dependent decisions (key set, JWT parameters, the <c>Require</c> flag) are read
/// lazily via <see cref="IOptionsMonitor{TOptions}"/> so hosts that layer configuration after
/// service registration (tests, reloads) are honoured~ 🌸.
/// </remarks>
public static class AuthServiceCollectionExtensions
{
    /// <summary>Adds the workflow API authentication + authorization surface~ 🔐.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The app configuration.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddWorkflowApiAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiAuthOptions>(configuration.GetSection("Api").GetSection("Auth"));

        services
            .AddAuthentication(AuthConstants.ApiKeyScheme)
            .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(AuthConstants.ApiKeyScheme, _ => { })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });

        // Lazily configure JWT validation from the live options~
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        // The role/Require evaluation happens at request time~
        services.AddSingleton<IAuthorizationHandler, RolePolicyHandler>();

        services.AddAuthorization(authz =>
        {
            authz.AddPolicy(AuthConstants.WorkflowReadPolicy, p => BuildPolicy(
                p, AuthConstants.AdminRole, AuthConstants.DeveloperRole, AuthConstants.ViewerRole));
            authz.AddPolicy(AuthConstants.WorkflowWritePolicy, p => BuildPolicy(
                p, AuthConstants.AdminRole, AuthConstants.DeveloperRole));
            authz.AddPolicy(AuthConstants.WorkflowExecutePolicy, p => BuildPolicy(
                p, AuthConstants.AdminRole, AuthConstants.DeveloperRole));
            authz.AddPolicy(AuthConstants.AdminPolicy, p => BuildPolicy(
                p, AuthConstants.AdminRole));
        });

        return services;
    }

    private static void BuildPolicy(AuthorizationPolicyBuilder builder, params string[] roles)
    {
        // Authenticate both schemes so a JWT bearer (non-default scheme) is honoured by the policy~
        builder.AddAuthenticationSchemes(AuthConstants.ApiKeyScheme, JwtBearerDefaults.AuthenticationScheme);
        builder.AddRequirements(new RolePolicyRequirement(roles));
    }
}
