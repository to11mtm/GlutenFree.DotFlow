// <copyright file="SecretVariableTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api.V1;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using LanguageExt;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Variables;
using Workflow.Core.Models;
using Workflow.Engine.Actors;
using Workflow.Persistence.Abstractions;
using Workflow.Persistence.Models;
using Workflow.Persistence.Sqlite.Serialization;
using Xunit;

/// <summary>
/// Plan 08 part 2 — secret variables at rest: stored encrypted, write-only through the API, no
/// earlier values kept (marking secret erases them, only when confirmed), "Save for future runs" of a
/// declared secret is encrypted automatically, "follow workflow declarations" becomes secret when a
/// workflow declares it and stays secret, and runs hide stored secrets too.
/// </summary>
public sealed class SecretVariableTests
{
    private const string Vars = "/api/v1/variables";
    private static readonly JsonSerializerOptions WfJson = LanguageExtJsonConverters.CreateOptions();

    [Fact]
    public async Task ASecret_IsStoredEncrypted_HiddenByTheApi_AndReadPlainByRuns()
    {
        using var host = new Host();
        var client = host.CreateClient();

        var put = await client.PutAsJsonAsync($"{Vars}/apiKey?scope=global", new { value = "abc12345", secretMode = "Secret" });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var dto = await put.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("isSecret").GetBoolean().Should().BeTrue();
        dto.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);

        var store = Store(host);
        var raw = await store.Inner.GetVariableAsync(VariableScope.Global, "apiKey");
        raw!.Value!.ToString().Should().StartWith(SecretVariableStore.Prefix).And.NotContain("abc12345");
        (await store.GetVariableAsync(VariableScope.Global, "apiKey"))!.Value.Should().Be("abc12345", because: "runs get the real value");
        (await store.GetAllVariablesAsync(VariableScope.Global))["apiKey"].Should().Be("abc12345");

        var list = await client.GetFromJsonAsync<JsonElement>($"{Vars}?scope=global");
        list.GetProperty("apiKey").GetString().Should().Be("••••••");
        (await client.GetFromJsonAsync<JsonElement>($"{Vars}/apiKey?scope=global")).GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        (await client.GetStringAsync($"{Vars}/apiKey/history?scope=global")).Should().NotContain("abc12345");
    }

    [Fact]
    public async Task MarkingAPlainVariableSecret_NeedsConfirmation_ThenErasesItsEarlierValues()
    {
        using var host = new Host();
        var client = host.CreateClient();
        (await client.PutAsJsonAsync($"{Vars}/token?scope=global", new { value = "old-one" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"{Vars}/token?scope=global", new { value = "old-two" })).EnsureSuccessStatusCode();
        var store = Store(host);

        var refused = await client.PutAsJsonAsync($"{Vars}/token/secret?scope=global", new { mode = "Secret" });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("versions").GetInt32().Should().Be(2);
        (await store.Inner.GetVariableHistoryAsync(VariableScope.Global, "token")).Should().HaveCount(2, because: "nothing is erased without confirmation");

        var confirmed = await client.PutAsJsonAsync($"{Vars}/token/secret?scope=global&confirmPurge=true", new { mode = "Secret" });

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        var raw = await store.Inner.GetVariableHistoryAsync(VariableScope.Global, "token");
        raw.Should().ContainSingle();
        raw[0].Value!.ToString().Should().StartWith(SecretVariableStore.Prefix);
        (await store.GetVariableAsync(VariableScope.Global, "token"))!.Value.Should().Be("old-two");

        var history = await client.GetStringAsync($"{Vars}/token/history?scope=global");
        history.Should().Contain("earlier values erased (2 versions)").And.Contain("marked secret").And.NotContain("old-one").And.NotContain("old-two");
    }

    [Fact]
    public async Task ASecretBecomingNotSecret_NeedsItsValueReEntered()
    {
        using var host = new Host();
        var client = host.CreateClient();
        (await client.PutAsJsonAsync($"{Vars}/pin?scope=global", new { value = "1234", secretMode = "Secret" })).EnsureSuccessStatusCode();

        var noValue = await client.PutAsJsonAsync($"{Vars}/pin/secret?scope=global", new { mode = "None" });
        noValue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noValue.Content.ReadAsStringAsync()).Should().Contain("can't be read back");

        var withValue = await client.PutAsJsonAsync($"{Vars}/pin/secret?scope=global", new { mode = "None", value = "5678" });
        withValue.StatusCode.Should().Be(HttpStatusCode.OK, await withValue.Content.ReadAsStringAsync());
        (await Store(host).Inner.GetVariableAsync(VariableScope.Global, "pin"))!.Value!.ToString().Should().Be("5678");
        (await client.GetFromJsonAsync<JsonElement>($"{Vars}/pin?scope=global")).GetProperty("value").GetString().Should().Be("5678");
    }

    [Fact]
    public async Task SaveForFutureRuns_OfADeclaredSecret_IsStoredEncrypted()
    {
        using var host = new Host();
        var client = host.CreateClient();
        var workflowId = await CreateWorkflowAsync(client, ("token", true));
        var store = Store(host);

        // what the engine does for "Save for future runs" (workflow scope)~
        await store.SetVariableAsync(VariableScope.ForWorkflow(workflowId), "token", "run-written-secret");

        var raw = await store.Inner.GetVariableAsync(VariableScope.ForWorkflow(workflowId), "token");
        raw!.Value!.ToString().Should().StartWith(SecretVariableStore.Prefix).And.NotContain("run-written-secret");
        host.Services.GetRequiredService<SecretVariableService>().Get(VariableScope.ForWorkflow(workflowId), "token")!.Mode
            .Should().Be(SecretVariableMode.Secret);
    }

    [Fact]
    public async Task FollowMode_BecomesSecretWhenAWorkflowDeclaresIt_AndStaysSecret()
    {
        using var host = new Host();
        var client = host.CreateClient();
        (await client.PutAsJsonAsync($"{Vars}/dbpass?scope=global", new { value = "plain-pass" })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"{Vars}/dbpass/secret?scope=global", new { mode = "FollowWorkflows" })).EnsureSuccessStatusCode();
        var store = Store(host);
        (await store.Inner.GetVariableAsync(VariableScope.Global, "dbpass"))!.Value!.ToString().Should().Be("plain-pass", because: "no workflow declares it secret yet");

        var impact = await client.PostAsJsonAsync($"{Vars}/secret-impact", new { workflowId = Guid.NewGuid(), secretNames = new[] { "dbpass" } });
        (await impact.Content.ReadAsStringAsync()).Should().Contain("dbpass");

        var workflowId = await CreateWorkflowAsync(client, ("dbpass", true));
        (await store.Inner.GetVariableAsync(VariableScope.Global, "dbpass"))!.Value!.ToString().Should().StartWith(SecretVariableStore.Prefix);
        (await client.GetFromJsonAsync<JsonElement>($"{Vars}/dbpass?scope=global")).GetProperty("isSecret").GetBoolean().Should().BeTrue();

        await UpdateWorkflowAsync(client, workflowId, ("dbpass", false));
        (await client.GetFromJsonAsync<JsonElement>($"{Vars}/dbpass?scope=global")).GetProperty("isSecret").GetBoolean()
            .Should().BeTrue(because: "once secret, a follow-mode variable stays secret");
    }

    [Fact]
    public void ARun_HidesStoredSecrets_EvenWhenItsWorkflowDoesntDeclareThem()
    {
        var definition = Definition(Guid.NewGuid());
        var redactor = SecretRedactor.For(
            definition,
            new[] { new KeyValuePair<string, object?>("apiKey", "abc12345") },
            new[] { "apiKey" });

        redactor.Text("calling with abc12345").Should().NotContain("abc12345");
    }

    private static SecretVariableStore Store(Host host) => (SecretVariableStore)host.Services.GetRequiredService<IVariableStore>();

    private static WorkflowDefinition Definition(Guid id, params (string Name, bool Secret)[] variables)
        => new(
            id,
            "secret-test-" + id.ToString("N")[..6],
            null,
            new Version(1, 0, 0),
            Arr.create(new NodeDefinition("n1", "builtin.passthrough", "N1", HashMap<string, JsonElement>.Empty)),
            Arr<ConnectionDefinition>.Empty,
            variables.Aggregate(HashMap<string, VariableDefinition>.Empty, (m, v) => m.AddOrUpdate(v.Name, new VariableDefinition(v.Name, PropertyType.String, IsSecret: v.Secret))));

    private static async Task<Guid> CreateWorkflowAsync(HttpClient client, params (string Name, bool Secret)[] variables)
    {
        var content = new StringContent(JsonSerializer.Serialize(Definition(Guid.Empty, variables), WfJson), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/workflows", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task UpdateWorkflowAsync(HttpClient client, Guid id, params (string Name, bool Secret)[] variables)
    {
        var content = new StringContent(JsonSerializer.Serialize(Definition(id, variables), WfJson), Encoding.UTF8, "application/json");
        var response = await client.PutAsync($"/api/v1/workflows/{id}", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Persistence:Provider", "sqlite");
            builder.UseSetting("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory());
        }
    }
}
