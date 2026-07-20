// <copyright file="ModulesClientManagementTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.UI.Modules;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Tests.UI.Api;
using Workflow.UI.Client.Api;
using Workflow.UI.Client.Api.Dtos;
using Xunit;

/// <summary>
/// 📦 Phase 3.6.0 — Tests for the management additions to <see cref="ModulesClient"/>
/// (upload / enable / disable / uninstall)~ ✨.
/// </summary>
public sealed class ModulesClientManagementTests
{
    private static ModuleDetailsDto Details(string id)
        => new(id, "Mod", "HTTP", "desc", "🔧", "1.3.0",
            new ModuleSchemaDto(new(), new(), new()), new List<string>(), true, new List<string> { "1.3.0" });

    [Fact]
    public async Task ModulesClient_Upload_SendsMultipartPackage()
    {
        var result = new ModuleInstallResultDto(Details("my.mod"), new List<string> { "unsigned" });
        var handler = FakeHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(result, ApiHttp.Json));
        var client = new ModulesClient(handler.CreateClient());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("PKfake"));
        var res = await client.UploadAsync("my.mod-1.3.0.wfmod", stream);

        res.Module.Id.Should().Be("my.mod");
        res.Warnings.Should().Contain("unsigned");
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().EndWith("/modules/upload");
        req.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        handler.Bodies[0].Should().Contain("name=package").And.Contain("my.mod-1.3.0.wfmod");
    }

    [Fact]
    public async Task ModulesClient_Enable_Posts_WithVersion()
    {
        var toggle = new ModuleToggleResultDto("my.mod", true, new List<string> { "1.3.0" });
        var handler = FakeHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(toggle, ApiHttp.Json));
        var client = new ModulesClient(handler.CreateClient());

        var res = await client.EnableAsync("my.mod", "1.3.0");

        res.Enabled.Should().BeTrue();
        var req = handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.ToString().Should().Contain("/modules/my.mod/enable").And.Contain("version=1.3.0");
    }

    [Fact]
    public async Task ModulesClient_Disable_Posts()
    {
        var toggle = new ModuleToggleResultDto("my.mod", false, new List<string> { "1.3.0" });
        var handler = FakeHttpMessageHandler.Json(System.Text.Json.JsonSerializer.Serialize(toggle, ApiHttp.Json));
        var client = new ModulesClient(handler.CreateClient());

        var res = await client.DisableAsync("my.mod");

        res.Enabled.Should().BeFalse();
        handler.Requests[0].RequestUri!.AbsolutePath.Should().EndWith("/modules/my.mod/disable");
    }

    [Fact]
    public async Task ModulesClient_Uninstall_Deletes()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new ModulesClient(handler.CreateClient());

        await client.UninstallAsync("my.mod");

        handler.Requests[0].Method.Should().Be(HttpMethod.Delete);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().EndWith("/modules/my.mod");
    }

    [Fact]
    public async Task ModulesClient_Upload_Duplicate_SurfacesConflict()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("{\"title\":\"Conflict\",\"detail\":\"Duplicate version 1.3.0 already installed.\"}", Encoding.UTF8, "application/problem+json"),
        });
        var client = new ModulesClient(handler.CreateClient());

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var act = () => client.UploadAsync("dup.wfmod", stream);

        var ex = await act.Should().ThrowAsync<ApiException>();
        ex.Which.Error.StatusCode.Should().Be(409);
        ex.Which.Error.Detail.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task ModulesClient_Enable_InvalidatesCache()
    {
        // First list is cached; after enable the cache must be dropped so a re-list hits the server again.
        var listJson = "[{\"id\":\"my.mod\",\"displayName\":\"Mod\",\"category\":\"HTTP\",\"description\":\"d\",\"icon\":\"🔧\",\"version\":\"1.0.0\",\"enabled\":true}]";
        var toggleJson = System.Text.Json.JsonSerializer.Serialize(new ModuleToggleResultDto("my.mod", false, new List<string> { "1.0.0" }), ApiHttp.Json);
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(toggleJson) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listJson) });
        var client = new ModulesClient(handler.CreateClient());

        await client.ListAsync();
        await client.ListAsync(); // cached — still one GET
        handler.Requests.FindAll(r => r.Method == HttpMethod.Get).Count.Should().Be(1);

        await client.DisableAsync("my.mod");
        await client.ListAsync(); // cache invalidated → a second GET

        handler.Requests.FindAll(r => r.Method == HttpMethod.Get).Count.Should().Be(2);
    }
}
