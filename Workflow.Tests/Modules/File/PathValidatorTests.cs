// <copyright file="PathValidatorTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules.FileSystem;

using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Workflow.Modules.Builtin.File;
using Workflow.Modules.Builtin.File.Internal;
using Xunit;

/// <summary>
/// 🛡️ Phase 2.5.a.0 — tests for <see cref="DefaultWorkflowPathValidator"/> and
/// <see cref="EncodingResolver"/>~ 📁✨.
/// </summary>
public sealed class PathValidatorTests : IDisposable
{
    private readonly string tempRoot;

    public PathValidatorTests()
    {
        this.tempRoot = Path.Combine(Path.GetTempPath(), "dotflow-pv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.tempRoot))
            {
                Directory.Delete(this.tempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup~ 🧹
        }
    }

    private static DefaultWorkflowPathValidator Build(FileSystemModuleOptions options)
        => new(Options.Create(options), NullLogger<DefaultWorkflowPathValidator>.Instance);

    [Fact]
    public void Validator_NoRootsConfigured_Unrestricted_AllowsAbsolutePath()
    {
        var v = Build(new FileSystemModuleOptions { UnrestrictedIfNoRoots = true });
        var target = Path.Combine(this.tempRoot, "a.txt");

        var result = v.ValidatePath(target, PathAccessIntent.Read);

        result.IsValid.Should().BeTrue();
        result.ResolvedPath.Should().Be(Path.GetFullPath(target));
    }

    [Fact]
    public void Validator_NoRoots_UnrestrictedDisabled_Fails()
    {
        var v = Build(new FileSystemModuleOptions { UnrestrictedIfNoRoots = false });

        v.ValidatePath(Path.Combine(this.tempRoot, "a.txt"), PathAccessIntent.Read)
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RootConfigured_PathInsideRoot_ResolvesAbsolute()
    {
        var v = Build(new FileSystemModuleOptions { AllowedRoots = new[] { this.tempRoot } });
        var target = Path.Combine(this.tempRoot, "sub", "a.txt");

        var result = v.ValidatePath(target, PathAccessIntent.Read);

        result.IsValid.Should().BeTrue();
        result.ResolvedPath.Should().Be(Path.GetFullPath(target));
    }

    [Fact]
    public void Validator_RootConfigured_PathOutsideRoot_Fails()
    {
        var v = Build(new FileSystemModuleOptions { AllowedRoots = new[] { this.tempRoot } });
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid().ToString("N"), "a.txt");

        v.ValidatePath(outside, PathAccessIntent.Read).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_DotDotEscape_Fails()
    {
        var v = Build(new FileSystemModuleOptions { AllowedRoots = new[] { this.tempRoot } });
        var escaping = Path.Combine(this.tempRoot, "sub", "..", "..", "etc", "passwd");

        v.ValidatePath(escaping, PathAccessIntent.Read).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_SiblingPrefixDir_Fails()
    {
        // Root "…/data" must not match sibling "…/data-evil"~ 🛡️
        var root = Path.Combine(this.tempRoot, "data");
        Directory.CreateDirectory(root);
        var v = Build(new FileSystemModuleOptions { AllowedRoots = new[] { root } });

        var sibling = Path.Combine(this.tempRoot, "data-evil", "a.txt");

        v.ValidatePath(sibling, PathAccessIntent.Read).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_WriteIntent_BlockedExtension_Fails()
    {
        var v = Build(new FileSystemModuleOptions { UnrestrictedIfNoRoots = true });
        var target = Path.Combine(this.tempRoot, "evil.ps1");

        v.ValidatePath(target, PathAccessIntent.Write).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ReadIntent_BlockedExtension_Allowed()
    {
        var v = Build(new FileSystemModuleOptions { UnrestrictedIfNoRoots = true });
        var target = Path.Combine(this.tempRoot, "script.ps1");

        v.ValidatePath(target, PathAccessIntent.Read).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_EmptyPath_Fails()
    {
        var v = Build(new FileSystemModuleOptions { UnrestrictedIfNoRoots = true });

        v.ValidatePath("  ", PathAccessIntent.Read).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf8")]
    [InlineData("utf-16")]
    [InlineData("ascii")]
    [InlineData("latin1")]
    [InlineData("iso-8859-1")]
    public void EncodingResolver_KnownAliases_Resolve(string key)
    {
        EncodingResolver.TryResolve(key, out var enc, out var err).Should().BeTrue();
        enc.Should().NotBeNull();
        err.Should().BeNull();
    }

    [Fact]
    public void EncodingResolver_NullKey_ReturnsDefaultUtf8NoBom()
    {
        EncodingResolver.TryResolve(null, out var enc, out _).Should().BeTrue();
        enc.GetPreamble().Should().BeEmpty("default UTF-8 must not emit a BOM~ 📄");
    }

    [Fact]
    public void EncodingResolver_UnknownKey_Fails()
    {
        EncodingResolver.TryResolve("klingon-9000", out _, out var err).Should().BeFalse();
        err.Should().NotBeNull();
    }
}
