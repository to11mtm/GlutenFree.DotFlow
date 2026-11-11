// <copyright file="DataProtectionKeyStorageTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Api;

using System;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Workflow.Api.Security;
using Workflow.Persistence.Abstractions;
using Xunit;

/// <summary>
/// Plan 08 part 1 — where the Data Protection keys live (they decrypt stored secrets and connection
/// strings): the persistence store by default (shared by every server, survives restarts), a folder,
/// or ASP.NET's defaults (dangerous, warned).
/// </summary>
public sealed class DataProtectionKeyStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dotflow-dp-" + Guid.NewGuid().ToString("N"));

    public DataProtectionKeyStorageTests() => Directory.CreateDirectory(this.root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public async Task ByDefault_TheKeysLiveInThePersistenceStore_SoAnotherServerCanDecrypt()
    {
        var database = $"Data Source={Path.Combine(this.root, "dotflow.db")};Pooling=False";
        string protectedText;
        using (var first = new Host(("Persistence:Provider", "sqlite"), ("Persistence:ConnectionString", database)))
        {
            first.CreateClient();
            protectedText = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("s3cret");
            (await first.Services.GetRequiredService<IBlobStore>().ExistsAsync(DataProtectionKeys.BlobKey)).Should().BeTrue(because: "the key ring is kept in the persistence store");
            var status = await first.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/status");
            status.GetProperty("dataProtection").GetProperty("keyStorage").GetString().Should().Be("PersistenceStore");
            status.GetProperty("dataProtection").GetProperty("warning").ValueKind.Should().Be(JsonValueKind.Null);
        }

        using var second = new Host(("Persistence:Provider", "sqlite"), ("Persistence:ConnectionString", database));
        second.CreateClient();
        second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedText)
            .Should().Be("s3cret", because: "the key ring is shared through the persistence store");
    }

    [Fact]
    public void APersistenceStoreInMemory_Warns()
    {
        using var host = new Host(("Persistence:Provider", "sqlite"), ("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory()));
        host.CreateClient();

        host.Services.GetRequiredService<DataProtectionKeyStatus>().Warning.Should().Contain("in memory");
    }

    [Fact]
    public void AFolder_HoldsTheKeys()
    {
        var folder = Path.Combine(this.root, "keys");
        using var host = new Host(
            ("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory()),
            ("DataProtection:KeyStorage", "Folder"),
            ("DataProtection:KeysPath", folder));
        host.CreateClient();

        host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("s3cret");

        Directory.EnumerateFiles(folder, "*.xml").Should().NotBeEmpty();
        var status = host.Services.GetRequiredService<DataProtectionKeyStatus>();
        status.Location.Should().Be(Path.GetFullPath(folder));
        status.Warning.Should().BeNull();
    }

    [Fact]
    public void AFolderWithoutAPath_RefusesToStart()
    {
        using var host = new Host(("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory()), ("DataProtection:KeyStorage", "Folder"));

        var start = () => host.CreateClient();

        start.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("DataProtection:KeysPath");
    }

    [Fact]
    public void TheDefaults_AreFlaggedAsDangerous()
    {
        using var host = new Host(("Persistence:ConnectionString", Workflow.Tests.TestSqlite.InMemory()), ("DataProtection:KeyStorage", "Default"));
        host.CreateClient();

        host.Services.GetRequiredService<DataProtectionKeyStatus>().Warning.Should().Contain("dangerous");
    }

    [Fact]
    public void TheStore_ImportsExistingKeysOnce_SoValuesEncryptedBeforeTheSwitchStayReadable()
    {
        var legacy = Path.Combine(this.root, "legacy");
        Directory.CreateDirectory(legacy);
        new XElement("key", new XAttribute("id", "old-key")).Save(Path.Combine(legacy, "key-old.xml"));
        var store = new FakeBlobStore();

        var repository = new BlobXmlRepository(store, legacy);
        repository.GetAllElements().Should().ContainSingle(e => (string?)e.Attribute("id") == "old-key");

        repository.StoreElement(new XElement("key", new XAttribute("id", "new-key")), "new");
        new BlobXmlRepository(store, legacyFolder: null).GetAllElements().Select(e => (string?)e.Attribute("id"))
            .Should().BeEquivalentTo("old-key", "new-key");
    }

    private sealed class Host : WebApplicationFactory<Program>
    {
        private readonly (string Key, string Value)[] settings;

        public Host(params (string Key, string Value)[] settings) => this.settings = settings;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in this.settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }

    private sealed class FakeBlobStore : IBlobStore
    {
        private readonly System.Collections.Generic.Dictionary<string, byte[]> blobs = new();

        public async Task<string> PutAsync(string key, Stream data, string? contentType = null, System.Threading.CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await data.CopyToAsync(buffer, ct);
            this.blobs[key] = buffer.ToArray();
            return key;
        }

        public Task<Stream?> GetAsync(string key, System.Threading.CancellationToken ct = default)
            => Task.FromResult<Stream?>(this.blobs.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

        public Task<bool> DeleteAsync(string key, System.Threading.CancellationToken ct = default) => Task.FromResult(this.blobs.Remove(key));

        public Task<bool> ExistsAsync(string key, System.Threading.CancellationToken ct = default) => Task.FromResult(this.blobs.ContainsKey(key));

        public Task<string> GeneratePresignedUrlAsync(string key, TimeSpan expiry, System.Threading.CancellationToken ct = default) => Task.FromResult(key);

        public async System.Collections.Generic.IAsyncEnumerable<BlobInfo> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
