// <copyright file="ModulePackCli.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tools.ModulePack;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Workflow.Modules.Packaging;
using Workflow.Modules.Security;

/// <summary>
/// 🔏 Stage 7.4 — the <c>dotflow-module</c> command line:
/// <list type="bullet">
///   <item><c>pack &lt;folder&gt;</c> — hash every file into <c>module.json</c>, optionally sign it, and zip a <c>.wfmod</c>.</item>
///   <item><c>verify &lt;file.wfmod&gt;</c> — check the hashes and who signed it (optionally against trusted certificates).</item>
/// </list>
/// </summary>
public static class ModulePackCli
{
    /// <summary>The environment variable holding a <c>.pfx</c> password by default.</summary>
    public const string DefaultPasswordVariable = "DOTFLOW_SIGN_PASSWORD";

    private const string Usage = """
        dotflow-module — pack and sign DotFlow module packages (.wfmod)

          dotflow-module pack <folder> [--out <file.wfmod>]
                         [--cert <publisher.pfx> [--password-env <VAR>]]
                         [--cert <publisher.crt.pem> --key <publisher.key.pem>]
                         [--chain <intermediate.pem>]...

              Writes the SHA-256 of every file in <folder> into its module.json ('contentHashes'),
              signs module.json when --cert is given (signature.p7s), and zips the package.
              A .pfx password is read from the environment variable named by --password-env
              (default DOTFLOW_SIGN_PASSWORD), never from the command line.

          dotflow-module verify <file.wfmod> [--trust <cert.pem | thumbprint>]... [--trust-root <root.pem>]...

              Checks the package's hashes and signature and shows who signed it. With --trust or
              --trust-root, exits 0 only when the signer is trusted, as a DotFlow server would decide.

        See docs/module-signing.md.
        """;

    /// <summary>Runs the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Where output goes.</param>
    /// <param name="stderr">Where errors go.</param>
    /// <param name="environment">Reads an environment variable (tests).</param>
    /// <returns>The exit code: 0 success, 1 failure, 2 usage error.</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (args.Length < 2 || args[0] is not ("pack" or "verify"))
        {
            stderr.WriteLine(Usage);
            return 2;
        }

        Dictionary<string, List<string>> options;
        try
        {
            options = ParseOptions(args.Skip(2).ToArray());
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            stderr.WriteLine(Usage);
            return 2;
        }

        try
        {
            return args[0] == "pack"
                ? Pack(args[1], options, stdout, stderr, environment)
                : Verify(args[1], options, stdout, stderr);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Pack(string folder, Dictionary<string, List<string>> options, TextWriter stdout, TextWriter stderr, Func<string, string?> environment)
    {
        X509Certificate2? signer = null;
        if (One(options, "--cert") is { } certPath)
        {
            if (One(options, "--key") is { } keyPath)
            {
                signer = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            }
            else
            {
                var variable = One(options, "--password-env") ?? DefaultPasswordVariable;
                signer = new X509Certificate2(certPath, environment(variable), X509KeyStorageFlags.Exportable);
            }

            // Windows can't sign with an ephemeral PEM key; re-import it as PFX~
            if (OperatingSystem.IsWindows())
            {
                signer = new X509Certificate2(signer.Export(X509ContentType.Pfx));
            }
        }

        var chain = All(options, "--chain").Select(path => X509Certificate2.CreateFromPemFile(path)).ToList();
        var result = ModulePackageBuilder.Build(folder, signer, chain);
        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                stderr.WriteLine($"error: {error}");
            }

            return 1;
        }

        var output = One(options, "--out") ?? $"{result.Manifest!.Id}-{result.Manifest.Version}.wfmod";
        File.WriteAllBytes(output, result.Package);
        stdout.WriteLine($"Packed {result.Manifest!.Id} {result.Manifest.Version}: {result.Files.Count} file(s) hashed -> {output}");
        stdout.WriteLine(result.Publisher is null
            ? "Not signed (pass --cert to sign it)."
            : $"Signed by {result.Publisher} (SHA-256 thumbprint {result.Thumbprint}).");
        return 0;
    }

    private static int Verify(string packagePath, Dictionary<string, List<string>> options, TextWriter stdout, TextWriter stderr)
    {
        var read = new ModulePackageReader().Read(File.ReadAllBytes(packagePath));
        if (!read.Success || read.Manifest is null)
        {
            foreach (var error in read.Errors)
            {
                stderr.WriteLine($"error: {error}");
            }

            return 1;
        }

        stdout.WriteLine($"{read.Manifest.Id} {read.Manifest.Version}: hashes OK ({read.Manifest.ContentHashes?.Count ?? 0} file(s)).");
        foreach (var warning in read.Warnings)
        {
            stdout.WriteLine($"warning: {warning}");
        }

        var trusted = All(options, "--trust")
            .Select(t => File.Exists(t) ? new TrustedCertificate { Pem = File.ReadAllText(t) } : new TrustedCertificate { Thumbprint = t })
            .ToList();
        var roots = All(options, "--trust-root").Select(r => new TrustedCertificate { Pem = File.ReadAllText(r) }).ToList();
        var checkTrust = trusted.Count + roots.Count > 0;

        var verifier = new PackageSignatureVerifier(new StaticTrustedPublisherProvider(new TrustedPublisherSet(trusted, roots, false)));
        var signature = verifier.Verify(read.ManifestBytes, read.SignatureBytes);
        switch (signature.Status)
        {
            case PackageSignatureStatus.Unsigned:
                stdout.WriteLine("Not signed.");
                return checkTrust ? 1 : 0;
            case PackageSignatureStatus.Invalid:
                stderr.WriteLine($"error: {string.Join(" ", signature.Messages)}");
                return 1;
            case PackageSignatureStatus.Verified:
                stdout.WriteLine($"Signed by {signature.Publisher} (SHA-256 thumbprint {signature.Thumbprint}): trusted ({signature.TrustSource}).");
                return 0;
            default:
                stdout.WriteLine(checkTrust
                    ? $"Signed by {signature.Publisher} (SHA-256 thumbprint {signature.Thumbprint}): NOT trusted. {string.Join(" ", signature.Messages)}"
                    : $"Signed by {signature.Publisher} (SHA-256 thumbprint {signature.Thumbprint}). Pass --trust or --trust-root to check it against trusted publishers.");
                return checkTrust ? 1 : 0;
        }
    }

    private static Dictionary<string, List<string>> ParseOptions(string[] args)
    {
        var known = new HashSet<string> { "--out", "--cert", "--key", "--password-env", "--chain", "--trust", "--trust-root" };
        var options = new Dictionary<string, List<string>>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!known.Contains(args[i]))
            {
                throw new ArgumentException($"Unknown option '{args[i]}'.");
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{args[i]}' needs a value.");
            }

            if (!options.TryGetValue(args[i], out var values))
            {
                options[args[i]] = values = new List<string>();
            }

            values.Add(args[++i]);
        }

        return options;
    }

    private static string? One(Dictionary<string, List<string>> options, string name)
        => options.TryGetValue(name, out var values) ? values[^1] : null;

    private static IEnumerable<string> All(Dictionary<string, List<string>> options, string name)
        => options.TryGetValue(name, out var values) ? values : Enumerable.Empty<string>();
}
