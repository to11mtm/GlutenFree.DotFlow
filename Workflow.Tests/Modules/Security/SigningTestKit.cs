// <copyright file="SigningTestKit.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Workflow.Modules.Packaging;
using Workflow.Tests.SampleModules;

/// <summary>
/// Stage 7.4 — test helpers for signed module packages: throwaway certificates (self-signed, a CA
/// root and an issued leaf) and the sample module's package, hashed and optionally signed.
/// </summary>
internal static class SigningTestKit
{
    internal static readonly string SampleDll = typeof(SampleLogModule).Assembly.Location;

    /// <summary>
    /// Builds the sample module's package (DLL, deps.json and an extra file), hashing every file and
    /// signing module.json when <paramref name="signer"/> is given.
    /// </summary>
    internal static byte[] Package(
        X509Certificate2? signer,
        X509Certificate2? chainCertificate = null,
        Func<string, string>? manifestAfterSigning = null,
        Action<Dictionary<string, byte[]>>? filesAfterSigning = null,
        string? leaveUnhashed = null)
    {
        var dllEntry = $"lib/{Path.GetFileName(SampleDll)}";
        var files = new Dictionary<string, byte[]>
        {
            [dllEntry] = File.ReadAllBytes(SampleDll),
            ["lib/extra.txt"] = Encoding.UTF8.GetBytes("readme"),
        };
        var deps = Path.ChangeExtension(SampleDll, ".deps.json");
        if (File.Exists(deps))
        {
            files[$"lib/{Path.GetFileName(deps)}"] = File.ReadAllBytes(deps);
        }

        var hashes = files
            .Where(f => f.Key != leaveUnhashed)
            .ToDictionary(f => f.Key, f => Convert.ToBase64String(SHA256.HashData(f.Value)));
        var manifest = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = "sample.log",
            ["version"] = "1.0.0",
            ["displayName"] = "sample.log",
            ["entryAssembly"] = dllEntry,
            ["contentHashes"] = hashes,
        });
        var manifestBytes = Encoding.UTF8.GetBytes(manifest);
        var signature = signer is null ? null : Sign(manifestBytes, signer, chainCertificate);

        if (manifestAfterSigning is not null)
        {
            manifestBytes = Encoding.UTF8.GetBytes(manifestAfterSigning(manifest));
        }

        filesAfterSigning?.Invoke(files);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, ModulePackageReader.ManifestFileName, manifestBytes);
            if (signature is not null)
            {
                Write(archive, ModulePackageReader.SignatureFileName, signature);
            }

            foreach (var (name, content) in files)
            {
                Write(archive, name, content);
            }
        }

        return ms.ToArray();
    }

    internal static byte[] Sign(byte[] content, X509Certificate2 signer, X509Certificate2? chainCertificate)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer) { IncludeOption = X509IncludeOption.EndCertOnly };
        if (chainCertificate is not null)
        {
            cmsSigner.Certificates.Add(chainCertificate);
        }

        cms.ComputeSignature(cmsSigner);
        return cms.Encode();
    }

    internal static void Write(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }

    internal static X509Certificate2 SelfSigned(string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
    }

    internal static X509Certificate2 CaRoot(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    internal static X509Certificate2 Issued(X509Certificate2 issuer, string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var issued = request.Create(issuer, notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(rsa);
    }
}
