// <copyright file="DataProtectionKeys.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workflow.Persistence.Abstractions;

/// <summary>🔑 Plan 08 — where ASP.NET Data Protection keeps its keys (they decrypt every secret DotFlow stores).</summary>
public enum DataProtectionKeyStorage
{
    /// <summary>The key ring lives in the persistence store (blob), shared by every server. The default.</summary>
    PersistenceStore,

    /// <summary>The key ring lives in a folder (<c>DataProtection:KeysPath</c>), e.g. a mounted volume.</summary>
    Folder,

    /// <summary>ASP.NET's defaults (user profile / <c>~/.aspnet</c>). <b>Dangerous</b>: lost on a container restart, different per server.</summary>
    Default,
}

/// <summary>🔑 Plan 08 — <c>DataProtection</c> settings.</summary>
public sealed class DataProtectionKeyOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>Gets or sets where the keys live.</summary>
    public DataProtectionKeyStorage KeyStorage { get; set; } = DataProtectionKeyStorage.PersistenceStore;

    /// <summary>Gets or sets the folder for <see cref="DataProtectionKeyStorage.Folder"/>.</summary>
    public string? KeysPath { get; set; }
}

/// <summary>🔑 Plan 08 — the key storage in force, for the status endpoint, Settings and the startup log.</summary>
/// <param name="Storage">The option.</param>
/// <param name="Location">Where the keys are (blob key, folder, or ASP.NET's default).</param>
/// <param name="Warning">Why this setup may lose keys, or null when it's safe.</param>
public sealed record DataProtectionKeyStatus(DataProtectionKeyStorage Storage, string Location, string? Warning);

/// <summary>🔑 Plan 08 — wires Data Protection's key storage from configuration.</summary>
public static class DataProtectionKeys
{
    /// <summary>The blob key of the shared key ring.</summary>
    public const string BlobKey = "settings/dataprotection-keys.xml";

    /// <summary>Adds Data Protection with the configured key storage.</summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddDotFlowDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new DataProtectionKeyOptions();
        configuration.GetSection(DataProtectionKeyOptions.SectionName).Bind(options);

        var builder = services.AddDataProtection();
        switch (options.KeyStorage)
        {
            case DataProtectionKeyStorage.Folder:
                if (string.IsNullOrWhiteSpace(options.KeysPath))
                {
                    throw new InvalidOperationException(
                        "DataProtection:KeyStorage is 'Folder' but DataProtection:KeysPath isn't set. Set it to a folder that survives restarts "
                        + "(e.g. a mounted volume shared by every server), or use 'PersistenceStore'.");
                }

                var folder = Path.GetFullPath(options.KeysPath);
                builder.PersistKeysToFileSystem(new DirectoryInfo(folder));
                services.AddSingleton(new DataProtectionKeyStatus(options.KeyStorage, folder, null));
                break;

            case DataProtectionKeyStorage.Default:
                services.AddSingleton(new DataProtectionKeyStatus(
                    options.KeyStorage,
                    "ASP.NET default (user profile / ~/.aspnet/DataProtection-Keys)",
                    "DataProtection:KeyStorage is 'Default' (dangerous): the keys that decrypt stored secrets and connection strings can be lost "
                    + "on a container restart and differ between servers, making those secrets unreadable. Use 'PersistenceStore' or 'Folder'."));
                break;

            default:
                services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp => new ConfigureOptions<KeyManagementOptions>(o =>
                {
                    if (sp.GetService<IBlobStore>() is { } store)
                    {
                        o.XmlRepository = new BlobXmlRepository(store, LegacyKeyFolder());
                    }
                }));
                services.AddSingleton(sp =>
                {
                    var blobs = sp.GetService<IBlobStore>();
                    return new DataProtectionKeyStatus(
                        options.KeyStorage,
                        $"persistence store ({BlobKey})",
                        blobs is null || blobs.IsInMemory
                            ? "DataProtection:KeyStorage is 'PersistenceStore' but the persistence store keeps blobs in memory, so the keys "
                              + "that decrypt stored secrets and connection strings are lost on restart. Configure a durable persistence provider, "
                              + "or use 'Folder'."
                            : null);
                });
                break;
        }

        return services;
    }

    /// <summary>ASP.NET's default key folder on this machine (imported once into the store), or null.</summary>
    /// <returns>The folder, or null.</returns>
    public static string? LegacyKeyFolder()
    {
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var home = Environment.GetEnvironmentVariable("HOME");
        var candidate = !string.IsNullOrEmpty(local)
            ? Path.Combine(local, "ASP.NET", "DataProtection-Keys")
            : !string.IsNullOrEmpty(home) ? Path.Combine(home, ".aspnet", "DataProtection-Keys") : null;
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }
}

/// <summary>
/// 🔑 Plan 08 — keeps the Data Protection key ring as one XML document in the blob store, so every
/// server shares it and it survives restarts. The first time (no document yet) it imports the keys
/// from ASP.NET's default folder, so values already encrypted before the switch stay readable.
/// </summary>
/// <remarks>
/// Keys are created rarely (about every 90 days); a write re-reads and merges, so two servers adding
/// a key at the same moment is the only (small) window for losing one.
/// </remarks>
public sealed class BlobXmlRepository : IXmlRepository
{
    private readonly IBlobStore blobs;
    private readonly string? legacyFolder;
    private readonly object gate = new();

    /// <summary>Initializes a new instance of the <see cref="BlobXmlRepository"/> class.</summary>
    /// <param name="blobs">The blob store.</param>
    /// <param name="legacyFolder">A folder of existing keys to import once (null = none).</param>
    public BlobXmlRepository(IBlobStore blobs, string? legacyFolder = null)
    {
        this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        this.legacyFolder = legacyFolder;
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (this.gate)
        {
            var document = this.Read();
            if (document is null)
            {
                var imported = this.ImportLegacy();
                if (imported.Count > 0)
                {
                    this.Write(new XElement("keys", imported));
                }

                return imported;
            }

            return document.Elements().ToList();
        }
    }

    /// <inheritdoc/>
    public void StoreElement(XElement element, string friendlyName)
    {
        lock (this.gate)
        {
            var document = this.Read() ?? new XElement("keys", this.ImportLegacy());
            document.Add(new XElement(element));
            this.Write(document);
        }
    }

    private XElement? Read()
    {
        using var stream = this.blobs.GetAsync(DataProtectionKeys.BlobKey).GetAwaiter().GetResult();
        return stream is null ? null : XElement.Load(stream);
    }

    private void Write(XElement document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting)));
        this.blobs.PutAsync(DataProtectionKeys.BlobKey, stream, "application/xml").GetAwaiter().GetResult();
    }

    private List<XElement> ImportLegacy()
    {
        if (this.legacyFolder is null || !Directory.Exists(this.legacyFolder))
        {
            return new List<XElement>();
        }

        var imported = new List<XElement>();
        foreach (var file in Directory.EnumerateFiles(this.legacyFolder, "*.xml"))
        {
            try
            {
                imported.Add(XElement.Load(file));
            }
            catch (System.Xml.XmlException)
            {
                // not a key file~
            }
        }

        return imported;
    }
}
