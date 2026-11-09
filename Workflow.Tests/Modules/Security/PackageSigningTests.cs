// <copyright file="PackageSigningTests.cs" company="GlutenFree">
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
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;
using Workflow.Modules.Security;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// Stage 7.4 (04 §9.5) — signed module packages: a detached CMS signature over <c>module.json</c>
/// (which hashes every file) by a trusted publisher (pinned certificate or CA root). A copied
/// strong-name token is no longer the only way to satisfy <c>RequireSigned</c>, and it's labelled
/// for what it is; every load re-checks the files against what was verified at install.
/// </summary>
public sealed class PackageSigningTests : IDisposable
{
    private static readonly string SampleDll = typeof(SampleLogModule).Assembly.Location;

    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "wfmod-sign-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch
        {
            // best effort (a loaded plugin DLL may still be locked)
        }
    }

    // ── Install ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SignedByAPinnedThumbprint_InstallsAsVerified_UnderRequireSigned()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, registry) = this.NewInstaller(requireSigned: true, Pinned(thumbprint: publisher.GetCertHashString(HashAlgorithmName.SHA256)));

        var result = await installer.InstallAsync(Package(publisher));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        registry.HasModule("sample.log").Should().BeTrue();
        result.Trust!.Trust.Should().Be(ModuleTrust.Verified);
        result.Trust.Publisher.Should().Be("Acme Modules");
        result.Trust.TrustSource.Should().Be(PackageTrustSource.PinnedCertificate);
        File.Exists(Path.Combine(installer.VersionDirectory("sample.log", new Version(1, 0, 0)), ModuleInstallRecord.FileName)).Should().BeTrue();
        installer.TrustFor("sample.log", new Version(1, 0, 0))!.Trust.Should().Be(ModuleTrust.Verified);
    }

    [Fact]
    public async Task SignedByAPinnedPem_InstallsAsVerified()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: true, Pinned(pem: publisher.ExportCertificatePem()));

        var result = await installer.InstallAsync(Package(publisher));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.Trust!.Trust.Should().Be(ModuleTrust.Verified);
    }

    [Fact]
    public async Task AManifestChangedAfterSigning_IsRefused_EvenWithoutRequireSigned()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, registry) = this.NewInstaller(requireSigned: false, Pinned(thumbprint: publisher.Thumbprint));

        var result = await installer.InstallAsync(Package(publisher, manifestAfterSigning: m => m.Replace("\"displayName\":\"sample.log\"", "\"displayName\":\"evil\"")));

        result.Success.Should().BeFalse();
        result.Reason.Should().Be(ModuleInstallFailureReason.InvalidPackage);
        result.Errors.Should().Contain(e => e.Contains("tampered"));
        registry.HasModule("sample.log").Should().BeFalse();
    }

    [Fact]
    public async Task AFileChangedAfterSigning_IsRefused()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: false, Pinned(thumbprint: publisher.Thumbprint));

        var result = await installer.InstallAsync(Package(publisher, filesAfterSigning: f => f["lib/extra.txt"] = Encoding.UTF8.GetBytes("changed")));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("mismatch"));
    }

    [Fact]
    public async Task ASignedPackage_MustHashEveryFile()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: false, Pinned(thumbprint: publisher.Thumbprint));

        var result = await installer.InstallAsync(Package(publisher, leaveUnhashed: "lib/extra.txt"));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("don't cover") && e.Contains("lib/extra.txt"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AnUntrustedSigner_WarnsByDefault_AndIsRefusedUnderRequireSigned(bool requireSigned, bool installs)
    {
        using var publisher = SelfSigned("Acme Modules");
        using var stranger = SelfSigned("Someone Else");
        var (installer, registry) = this.NewInstaller(requireSigned, Pinned(thumbprint: publisher.Thumbprint));

        var result = await installer.InstallAsync(Package(stranger));

        result.Success.Should().Be(installs);
        registry.HasModule("sample.log").Should().Be(installs);
        if (installs)
        {
            result.Trust!.Trust.Should().Be(ModuleTrust.Untrusted);
            result.Warnings.Should().Contain(w => w.Contains("Someone Else") && w.Contains("isn't a trusted publisher"));
        }
        else
        {
            result.Reason.Should().Be(ModuleInstallFailureReason.Untrusted);
            result.Errors.Should().Contain(e => e.Contains("Someone Else"));
        }
    }

    [Fact]
    public async Task ASignerIssuedByATrustedCaRoot_IsVerified()
    {
        using var root = CaRoot("Acme Root CA");
        using var publisher = Issued(root, "Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: true, Roots(root));

        var result = await installer.InstallAsync(Package(publisher, root));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.Trust!.TrustSource.Should().Be(PackageTrustSource.CertificateAuthority);
    }

    [Fact]
    public async Task ASignerIssuedByAnotherRoot_IsRefusedUnderRequireSigned()
    {
        using var root = CaRoot("Acme Root CA");
        using var otherRoot = CaRoot("Other Root CA");
        using var publisher = Issued(otherRoot, "Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: true, Roots(root));

        var result = await installer.InstallAsync(Package(publisher, otherRoot));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("doesn't chain to a trusted CA root"));
    }

    [Fact]
    public async Task AnExpiredSigner_IsRefusedAtInstall_UnderRequireSigned()
    {
        using var root = CaRoot("Acme Root CA");
        using var expired = Issued(root, "Acme Modules", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
        var (installer, _) = this.NewInstaller(requireSigned: true, Roots(root));
        (await installer.InstallAsync(Package(expired, root))).Success.Should().BeFalse();

        using var pinnedExpired = SelfSigned("Acme Modules", DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(-1));
        var (installer2, _) = this.NewInstaller(requireSigned: true, Pinned(thumbprint: pinnedExpired.Thumbprint), subfolder: "b");
        var result = await installer2.InstallAsync(Package(pinnedExpired));
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("expired"));
    }

    [Fact]
    public async Task AStrongNameTokenMatch_IsStillAccepted_ButLabelledKeyPinOnly()
    {
        var (installer, registry) = this.NewInstaller(requireSigned: true, TrustedPublisherSet.Empty, assemblyVerifier: new TokenTrusted());

        var result = await installer.InstallAsync(Package(signer: null));

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        registry.HasModule("sample.log").Should().BeTrue();
        result.Trust!.Trust.Should().Be(ModuleTrust.KeyPinOnly);
        result.Warnings.Should().Contain(w => w.Contains("not proof of authorship"));
    }

    // ── Startup re-load ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rehydrate_UnchangedVerifiedPackage_LoadsAsVerified()
    {
        using var publisher = SelfSigned("Acme Modules");
        var trusted = Pinned(thumbprint: publisher.Thumbprint);
        var (installer, _) = this.NewInstaller(requireSigned: true, trusted);
        (await installer.InstallAsync(Package(publisher))).Success.Should().BeTrue();

        var (restarted, registry) = this.NewInstaller(requireSigned: true, trusted);
        await restarted.RehydrateAsync();

        registry.HasModule("sample.log").Should().BeTrue();
        restarted.TrustFor("sample.log", new Version(1, 0, 0))!.Trust.Should().Be(ModuleTrust.Verified);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Rehydrate_AFileChangedOnDisk_WarnsByDefault_AndIsNotLoadedUnderRequireSigned(bool requireSigned, bool loads)
    {
        using var publisher = SelfSigned("Acme Modules");
        var trusted = Pinned(thumbprint: publisher.Thumbprint);
        var (installer, _) = this.NewInstaller(requireSigned: false, trusted);
        (await installer.InstallAsync(Package(publisher))).Success.Should().BeTrue();
        var versionDir = installer.VersionDirectory("sample.log", new Version(1, 0, 0));
        File.WriteAllText(Path.Combine(versionDir, "lib", "dropped.dll"), "not from the publisher");

        var (restarted, registry) = this.NewInstaller(requireSigned, trusted);
        await restarted.RehydrateAsync();

        registry.HasModule("sample.log").Should().Be(loads);
        var trust = restarted.TrustFor("sample.log", new Version(1, 0, 0))!;
        trust.Trust.Should().Be(ModuleTrust.ChangedOnDisk);
        trust.Messages.Should().Contain(m => m.Contains("added lib/dropped.dll"));
    }

    [Fact]
    public async Task Rehydrate_APublisherNoLongerTrusted_IsNotLoadedUnderRequireSigned()
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, _) = this.NewInstaller(requireSigned: true, Pinned(thumbprint: publisher.Thumbprint));
        (await installer.InstallAsync(Package(publisher))).Success.Should().BeTrue();

        var (restarted, registry) = this.NewInstaller(requireSigned: true, TrustedPublisherSet.Empty);
        await restarted.RehydrateAsync();

        registry.HasModule("sample.log").Should().BeFalse();
        restarted.TrustFor("sample.log", new Version(1, 0, 0))!.Messages
            .Should().Contain(m => m.Contains("no longer trusted"));
    }

    // ── Hot reload ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, ModuleReloadOutcome.Reloaded)]
    [InlineData(true, ModuleReloadOutcome.Refused)]
    public async Task HotReload_OfAChangedPackage_WarnsByDefault_AndIsRefusedUnderRequireSigned(bool requireSigned, ModuleReloadOutcome expected)
    {
        using var publisher = SelfSigned("Acme Modules");
        var (installer, registry, loader) = this.NewInstallerWithLoader(requireSigned, Pinned(thumbprint: publisher.Thumbprint));
        (await installer.InstallAsync(Package(publisher))).Success.Should().BeTrue();
        var versionDir = installer.VersionDirectory("sample.log", new Version(1, 0, 0));
        File.WriteAllText(Path.Combine(versionDir, "lib", "dropped.dll"), "not from the publisher");
        var entryDll = Path.Combine(versionDir, "lib", Path.GetFileName(SampleDll));

        var service = new ModuleReloadService(loader, registry, new Idle(), pollIntervalMs: 10, maxWaitMs: 1000, gate: installer);
        var outcome = await service.ReloadAsync(entryDll);

        outcome.Should().Be(expected);
        registry.HasModule("sample.log").Should().BeTrue(because: "a refused reload leaves the current version loaded");
        installer.TrustFor("sample.log", new Version(1, 0, 0))!.Trust.Should().Be(ModuleTrust.ChangedOnDisk);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TheGate_ForALooseUnsignedDll_WarnsByDefault_AndRefusesUnderRequireSigned(bool requireSigned, bool allowed)
    {
        var (installer, _) = this.NewInstaller(requireSigned, TrustedPublisherSet.Empty);

        var check = installer.CheckBeforeLoad(SampleDll);

        check.Allowed.Should().Be(allowed);
        check.Trust.Trust.Should().Be(ModuleTrust.Unsigned);
        check.Messages.Should().Contain(m => m.Contains("not strong-name"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private (ModulePackageInstaller Installer, InMemoryModuleRegistry Registry) NewInstaller(
        bool requireSigned,
        TrustedPublisherSet publishers,
        IAssemblyVerifier? assemblyVerifier = null,
        string subfolder = "a")
    {
        var (installer, registry, _) = this.NewInstallerWithLoader(requireSigned, publishers, assemblyVerifier, subfolder);
        return (installer, registry);
    }

    private (ModulePackageInstaller Installer, InMemoryModuleRegistry Registry, AssemblyModuleLoader Loader) NewInstallerWithLoader(
        bool requireSigned,
        TrustedPublisherSet publishers,
        IAssemblyVerifier? assemblyVerifier = null,
        string subfolder = "a")
    {
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var options = new ModulePackagingOptions { PackagesPath = Path.Combine(this.tempRoot, subfolder), RequireSigned = requireSigned };
        var installer = new ModulePackageInstaller(
            loader,
            registry,
            options,
            verifier: assemblyVerifier ?? new StrongNameVerifier(),
            signatureVerifier: new PackageSignatureVerifier(new StaticTrustedPublisherProvider(publishers)));
        return (installer, registry, loader);
    }

    private static TrustedPublisherSet Pinned(string? thumbprint = null, string? pem = null)
        => new(new[] { new TrustedCertificate { Name = "Acme", Thumbprint = thumbprint, Pem = pem } }, Array.Empty<TrustedCertificate>(), false);

    private static TrustedPublisherSet Roots(X509Certificate2 root)
        => new(Array.Empty<TrustedCertificate>(), new[] { new TrustedCertificate { Name = "Root", Pem = root.ExportCertificatePem() } }, false);

    /// <summary>
    /// Builds the sample module's package (DLL, deps.json and an extra file), hashing every file and
    /// signing module.json when <paramref name="signer"/> is given.
    /// </summary>
    private static byte[] Package(
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

    private static byte[] Sign(byte[] content, X509Certificate2 signer, X509Certificate2? chainCertificate)
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

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }

    private static X509Certificate2 SelfSigned(string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
    }

    private static X509Certificate2 CaRoot(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static X509Certificate2 Issued(X509Certificate2 issuer, string name, DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var issued = request.Create(issuer, notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(rsa);
    }

    /// <summary>An assembly verifier that reports a trusted strong-name token (the kept, weaker option).</summary>
    private sealed class TokenTrusted : IAssemblyVerifier
    {
        public AssemblyVerificationResult Verify(string assemblyPath) => new(true, "0123456789abcdef", true, Array.Empty<string>());
    }

    private sealed class Idle : IActiveExecutionTracker
    {
        public bool HasActiveExecutions => false;
    }
}
