// <copyright file="ModulePackToolTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Loading;
using Workflow.Modules.Packaging;
using Workflow.Modules.Security;
using Workflow.Tools.ModulePack;
using Xunit;
using static Workflow.Tests.Modules.Security.SigningTestKit;

/// <summary>
/// Stage 7.4 — the pack-and-sign tooling: <see cref="ModulePackageBuilder"/> hashes every file into
/// <c>module.json</c> and signs it, and the <c>dotflow-module</c> CLI (<see cref="ModulePackCli"/>)
/// packs with a PEM key or a password-protected PFX and verifies packages against trusted publishers.
/// </summary>
public sealed class ModulePackToolTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wfmod-tool-" + Guid.NewGuid().ToString("N"));

    public ModulePackToolTests()
    {
        var lib = Path.Combine(this.root, "mod", "lib");
        Directory.CreateDirectory(lib);
        File.Copy(SampleDll, Path.Combine(lib, Path.GetFileName(SampleDll)));
        var deps = Path.ChangeExtension(SampleDll, ".deps.json");
        if (File.Exists(deps))
        {
            File.Copy(deps, Path.Combine(lib, Path.GetFileName(deps)));
        }

        File.WriteAllText(Path.Combine(lib, "readme.txt"), "hello");
        File.WriteAllText(
            Path.Combine(this.root, "mod", ModulePackageReader.ManifestFileName),
            $$"""{ "id": "sample.log", "version": "1.0.0", "displayName": "Sample", "entryAssembly": "lib/{{Path.GetFileName(SampleDll)}}" }""");
    }

    private string Folder => Path.Combine(this.root, "mod");

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch
        {
            // best effort (a loaded plugin DLL may still be locked)
        }
    }

    [Fact]
    public void TheBuilder_HashesEveryFile()
    {
        var result = ModulePackageBuilder.Build(this.Folder);

        result.Success.Should().BeTrue(string.Join("; ", result.Errors));
        result.Manifest!.ContentHashes!.Keys.Should().BeEquivalentTo(result.Files);
        result.Files.Should().Contain("lib/readme.txt");
        using var zip = new ZipArchive(new MemoryStream(result.Package));
        zip.GetEntry(ModulePackageReader.SignatureFileName).Should().BeNull(because: "it wasn't signed");
    }

    [Fact]
    public async Task ABuiltSignedPackage_InstallsAsVerified()
    {
        using var publisher = SelfSigned("Acme Modules");
        var result = ModulePackageBuilder.Build(this.Folder, publisher);
        result.Success.Should().BeTrue(string.Join("; ", result.Errors));

        var registry = new InMemoryModuleRegistry();
        var installer = new ModulePackageInstaller(
            new AssemblyModuleLoader(registry),
            registry,
            new ModulePackagingOptions { PackagesPath = Path.Combine(this.root, "packages"), RequireSigned = true },
            verifier: new StrongNameVerifier(),
            signatureVerifier: new PackageSignatureVerifier(new StaticTrustedPublisherProvider(
                new TrustedPublisherSet(new[] { new TrustedCertificate { Thumbprint = publisher.Thumbprint } }, Array.Empty<TrustedCertificate>(), false))));

        var install = await installer.InstallAsync(result.Package);

        install.Success.Should().BeTrue(string.Join("; ", install.Errors));
        install.Trust!.Trust.Should().Be(ModuleTrust.Verified);
    }

    [Fact]
    public void TheBuilder_RefusesAFolderWithoutItsEntryAssembly()
    {
        File.Delete(Path.Combine(this.Folder, "lib", Path.GetFileName(SampleDll)));

        var result = ModulePackageBuilder.Build(this.Folder);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("entry assembly"));
    }

    [Fact]
    public void TheCli_PacksWithAPemKey_AndVerifiesAgainstTheTrustedCertificate()
    {
        var (certPem, keyPem) = this.PemPair("Acme Modules", "acme");
        var (otherPem, _) = this.PemPair("Someone Else", "other");
        var package = Path.Combine(this.root, "out.wfmod");

        var (packExit, packOut, _) = Run("pack", this.Folder, "--out", package, "--cert", certPem, "--key", keyPem);
        packExit.Should().Be(0);
        packOut.Should().Contain("Signed by Acme Modules");

        var (trustedExit, trustedOut, _) = Run("verify", package, "--trust", certPem);
        trustedExit.Should().Be(0);
        trustedOut.Should().Contain("hashes OK").And.Contain("trusted (PinnedCertificate)");

        var (otherExit, otherOut, _) = Run("verify", package, "--trust", otherPem);
        otherExit.Should().Be(1);
        otherOut.Should().Contain("NOT trusted");
    }

    [Fact]
    public void TheCli_ReadsAPfxPasswordFromTheEnvironment_NotTheCommandLine()
    {
        using var publisher = SelfSigned("Acme Modules");
        var pfx = Path.Combine(this.root, "acme.pfx");
        File.WriteAllBytes(pfx, publisher.Export(X509ContentType.Pfx, "s3cret"));
        var package = Path.Combine(this.root, "pfx.wfmod");

        var env = new Dictionary<string, string?> { ["MY_PFX_PASSWORD"] = "s3cret" };
        var (exit, output, errors) = Run(env, "pack", this.Folder, "--out", package, "--cert", pfx, "--password-env", "MY_PFX_PASSWORD");

        exit.Should().Be(0, errors);
        output.Should().Contain("Signed by Acme Modules");
        Run("verify", package, "--trust", publisher.GetCertHashString(HashAlgorithmName.SHA256)).Exit.Should().Be(0);
    }

    [Fact]
    public void TheCli_RejectsUnknownOptions()
    {
        var (exit, _, errors) = Run("pack", this.Folder, "--password", "s3cret");

        exit.Should().Be(2);
        errors.Should().Contain("Unknown option '--password'");
    }

    [Fact]
    public void TheCli_ReportsATamperedPackage()
    {
        var package = Path.Combine(this.root, "t.wfmod");
        Run("pack", this.Folder, "--out", package).Exit.Should().Be(0);
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("lib/readme.txt")!;
            entry.Delete();
            using var stream = zip.CreateEntry("lib/readme.txt").Open();
            stream.Write("changed"u8);
        }

        var (exit, _, errors) = Run("verify", package);

        exit.Should().Be(1);
        errors.Should().Contain("mismatch");
    }

    private static (int Exit, string Output, string Errors) Run(params string[] args) => Run(new Dictionary<string, string?>(), args);

    private static (int Exit, string Output, string Errors) Run(Dictionary<string, string?> environment, params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var exit = ModulePackCli.Run(args, output, errors, name => environment.TryGetValue(name, out var value) ? value : null);
        return (exit, output.ToString(), errors.ToString());
    }

    private (string Cert, string Key) PemPair(string name, string file)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var cert = Path.Combine(this.root, file + ".crt.pem");
        var key = Path.Combine(this.root, file + ".key.pem");
        File.WriteAllText(cert, certificate.ExportCertificatePem());
        File.WriteAllText(key, rsa.ExportPkcs8PrivateKeyPem());
        return (cert, key);
    }
}
