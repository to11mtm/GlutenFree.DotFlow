// <copyright file="ApiFoundationTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Auth;
using Workflow.Api.Contracts;
using Workflow.Api.V1;
using Workflow.Modules.Abstractions;
using Workflow.Persistence.Models;
using Xunit;

/// <summary>
/// 🛠️ Phase 2.7.0 — foundation tests: module-registry DI, caller identity, DTO helpers, pagination~ ✨.
/// </summary>
public sealed class ApiFoundationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiFoundationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Fact]
    public void ModuleRegistry_Registered_ContainsBuiltinsAndHostFamilies()
    {
        var registry = this.factory.Services.GetRequiredService<IModuleRegistry>();

        // A core builtin (HTTP family)~
        registry.HasModule("builtin.http.request").Should().BeTrue();

        // A Phase 2.6 transform-family module (host-wired via AddWorkflowModules)~
        registry.HasModule("builtin.transform.map").Should().BeTrue();

        // A Phase 2.4 database-family module (host-wired via AddDatabaseModules → DI IWorkflowModule)~
        registry.HasModule("builtin.database.query").Should().BeTrue();

        registry.GetAllModules().Should().NotBeEmpty();
    }

    [Fact]
    public void CallerIdentity_HeaderOverride_Wins()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Caller-Id"] = "alice";

        ctx.ResolveCallerId().Should().Be("alice");
    }

    [Fact]
    public void CallerIdentity_NoHeaderNoAuth_FallsBackToSystem()
    {
        var ctx = new DefaultHttpContext();

        ctx.ResolveCallerId().Should().Be(CallerIdentity.SystemCaller);
    }

    [Fact]
    public void JsonTypeHelpers_TypeAndVersion_Render()
    {
        JsonTypeHelpers.TypeName(typeof(string)).Should().Be("System.String");
        JsonTypeHelpers.TypeName(null).Should().BeNull();
        JsonTypeHelpers.VersionString(new Version(1, 2, 3)).Should().Be("1.2.3");
        JsonTypeHelpers.VersionString(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null, 1, Pagination.DefaultPageSize)]
    [InlineData(3, 25, 3, 25)]
    [InlineData(0, 9999, 1, Pagination.MaxPageSize)]
    public void PaginationBinding_ClampsAndDefaults(int? page, int? pageSize, int expectedPage, int expectedSize)
    {
        var p = PaginationBinding.From(page, pageSize);
        p.Page.Should().Be(expectedPage);
        p.PageSize.Should().Be(expectedSize);
    }
}
