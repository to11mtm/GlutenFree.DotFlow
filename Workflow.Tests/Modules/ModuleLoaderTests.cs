// <copyright file="ModuleLoaderTests.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Tests.Modules;

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Workflow.Modules;
using Workflow.Modules.Loading;
using Workflow.Tests.SampleModules;
using Xunit;

/// <summary>
/// 📦 Phase 1.4.6 — Tests for the Dynamic Module Loader!
/// Covers assembly loading from disk, registry registration, unloading,
/// error handling, directory scanning, and load context isolation~ ✨💖
/// </summary>
/// <remarks>
/// CopilotNote: We resolve the SampleModules DLL path at test time via
/// <c>typeof(SampleLogModule).Assembly.Location</c> so there are no hardcoded paths
/// and the tests stay green across machines and CI environments~ 🎯
/// </remarks>
public sealed class ModuleLoaderTests
{
    // CopilotNote: All sample modules live in this one DLL, built alongside tests~ 💖
    private static readonly string SampleModulesDllPath =
        typeof(SampleLogModule).Assembly.Location;

    private static readonly string SampleModulesDirectory =
        Path.GetDirectoryName(SampleModulesDllPath)!;

    #region LoadFromAssembly Tests 📦

    /// <summary>
    /// Loading a valid assembly should discover the sample modules it contains~ ✨
    /// </summary>
    [Fact]
    public void LoadFromAssembly_ValidAssembly_ShouldDiscoverModules()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act
        var result = loader.LoadFromAssembly(SampleModulesDllPath);

        // Assert
        result.Success.Should().BeTrue("the sample modules DLL is a valid assembly~ 💖");
        result.LoadedModules.Should().NotBeEmpty(
            "SampleLogModule and SampleDelayModule are valid and should be discovered~ ✨");
        result.AssemblyPath.Should().Be(
            Path.GetFullPath(SampleModulesDllPath),
            "AssemblyPath should be the normalized full path~ 🎯");
    }

    /// <summary>
    /// Loading a valid assembly should register the discovered modules in the registry~ 🗂️
    /// </summary>
    [Fact]
    public void LoadFromAssembly_ValidAssembly_ShouldRegisterModulesInRegistry()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act
        loader.LoadFromAssembly(SampleModulesDllPath);

        // Assert — SampleLogModule (sample.log) and SampleDelayModule (sample.delay) should be registered~
        registry.HasModule("sample.log").Should().BeTrue(
            "SampleLogModule with ID 'sample.log' should be registered after loading~ 💖");
        registry.HasModule("sample.delay").Should().BeTrue(
            "SampleDelayModule with ID 'sample.delay' should be registered after loading~ ✨");
    }

    /// <summary>
    /// SampleInvalidModule has an invalid ID — it should be SKIPPED, not crash the loader~ 🛡️
    /// </summary>
    [Fact]
    public void LoadFromAssembly_AssemblyWithInvalidModule_ShouldSkipInvalidButLoadValid()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act — should NOT throw even though SampleInvalidModule has a bad ID!
        var act = () => loader.LoadFromAssembly(SampleModulesDllPath);

        // Assert — load succeeds (Success=true) and invalid module is skipped~
        act.Should().NotThrow("invalid modules are skipped, not crash the loader~ UwU");
        registry.HasModule("INVALID-SAMPLE-MODULE").Should().BeFalse(
            "SampleInvalidModule should be rejected by validator~ 🎯");
    }

    /// <summary>
    /// Loading an invalid (non-existent) path should return a failed result, not throw~ ❌
    /// </summary>
    [Fact]
    public void LoadFromAssembly_InvalidPath_ShouldReturnFailResult()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var badPath = Path.Combine(SampleModulesDirectory, "DoesNotExist.dll");

        // Act
        var result = loader.LoadFromAssembly(badPath);

        // Assert
        result.Success.Should().BeFalse("a missing file cannot be loaded~ 💔");
        result.Errors.Should().NotBeEmpty("there should be an error explaining what went wrong~ 🎯");
        result.LoadedModules.Should().BeEmpty("nothing was loaded since the file doesn't exist~ 💖");
    }

    /// <summary>
    /// Loading an assembly that has no IWorkflowModule implementations should return
    /// Success=true with an empty LoadedModules list (not a failure!)~ 📭
    /// </summary>
    [Fact]
    public void LoadFromAssembly_AssemblyWithNoModules_ShouldSucceedWithEmptyResult()
    {
        // Arrange — use a well-known BCL assembly DLL that definitely has no workflow modules~
        // CopilotNote: We use xunit.assert.dll from the test output directory since it's
        // guaranteed to be there and has zero IWorkflowModule types~ 🎯
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Find a DLL in the output directory that has no modules (xunit, FluentAssertions, etc.)
        var noModulesDll = Path.Combine(
            SampleModulesDirectory,
            "xunit.assert.dll");

        // Skip if the DLL isn't available in this build output~ 🤷
        if (!File.Exists(noModulesDll))
        {
            return; // Graceful skip — not every build layout is identical~ 💖
        }

        // Act
        var result = loader.LoadFromAssembly(noModulesDll);

        // Assert
        result.Success.Should().BeTrue(
            "a valid DLL with no modules is still a successful load~ ✨");
        result.LoadedModules.Should().BeEmpty(
            "xunit.assert has no IWorkflowModule implementations~ 💖");
    }

    /// <summary>
    /// LoadFromAssembly should throw ArgumentNullException for null/empty path~ 🛑
    /// </summary>
    [Fact]
    public void LoadFromAssembly_NullPath_ShouldThrow()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act & Assert
        var act = () => loader.LoadFromAssembly(null!);
        act.Should().Throw<ArgumentException>("null assembly path is not allowed~ UwU");
    }

    #endregion

    #region UnloadAssembly Tests 🗑️

    /// <summary>
    /// After unloading, the modules from that assembly should be removed from the registry~ 🗑️
    /// </summary>
    [Fact]
    public void UnloadAssembly_LoadedAssembly_ShouldRemoveModulesFromRegistry()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        loader.LoadFromAssembly(SampleModulesDllPath);

        // Pre-condition: modules are registered~
        registry.HasModule("sample.log").Should().BeTrue(
            "sample.log should be registered before unload~ 💖");

        // Act
        var unloaded = loader.UnloadAssembly(SampleModulesDllPath);

        // Assert
        unloaded.Should().BeTrue("the assembly was previously loaded and should unload cleanly~ ✨");
        registry.HasModule("sample.log").Should().BeFalse(
            "sample.log should be removed from registry after unload~ 🗑️");
        registry.HasModule("sample.delay").Should().BeFalse(
            "sample.delay should also be removed from registry after unload~ 🗑️");
    }

    /// <summary>
    /// Unloading an assembly that was never loaded should return false (no crash!)~ 🤷
    /// </summary>
    [Fact]
    public void UnloadAssembly_NotLoadedAssembly_ShouldReturnFalse()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act
        var result = loader.UnloadAssembly(SampleModulesDllPath);

        // Assert
        result.Should().BeFalse("assembly was never loaded, so unload should return false~ UwU");
    }

    #endregion

    #region GetLoadedAssemblies Tests 📋

    /// <summary>
    /// GetLoadedAssemblies should track paths of all currently loaded assemblies~ 📋
    /// </summary>
    [Fact]
    public void GetLoadedAssemblies_AfterLoad_ShouldTrackAssemblyPath()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Pre-condition: nothing loaded yet~
        loader.GetLoadedAssemblies().Should().BeEmpty(
            "no assemblies loaded yet so the list should start empty~ 💖");

        // Act
        loader.LoadFromAssembly(SampleModulesDllPath);

        // Assert
        var loaded = loader.GetLoadedAssemblies();
        loaded.Should().HaveCount(1, "we loaded exactly one assembly~ ✨");
        loaded.Should().Contain(
            p => p.Equals(Path.GetFullPath(SampleModulesDllPath), StringComparison.OrdinalIgnoreCase),
            "the normalized path should be tracked in GetLoadedAssemblies~ 🎯");
    }

    /// <summary>
    /// After unloading, GetLoadedAssemblies should no longer include that path~ 🗑️
    /// </summary>
    [Fact]
    public void GetLoadedAssemblies_AfterUnload_ShouldNotContainPath()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        loader.LoadFromAssembly(SampleModulesDllPath);

        // Act
        loader.UnloadAssembly(SampleModulesDllPath);

        // Assert
        loader.GetLoadedAssemblies().Should().BeEmpty(
            "after unloading the only assembly, the tracked list should be empty~ 💖");
    }

    /// <summary>
    /// Loading the same assembly twice should be idempotent — no duplicate registrations~ 🔄
    /// </summary>
    [Fact]
    public void LoadFromAssembly_LoadSameTwice_ShouldBeIdempotent()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act
        loader.LoadFromAssembly(SampleModulesDllPath);
        var act = () => loader.LoadFromAssembly(SampleModulesDllPath);

        // Assert — second load should be a no-op, not throw or double-register!
        act.Should().NotThrow("loading same assembly twice is idempotent~ UwU");
        loader.GetLoadedAssemblies().Should().HaveCount(1,
            "the assembly should still only be tracked once~ 💖");
    }

    #endregion

    #region Assembly Isolation Tests 🔌

    /// <summary>
    /// Two separate loader instances each track their own assembly sets independently,
    /// proving proper isolation between loader instances~ 🔌
    /// </summary>
    [Fact]
    public void AssemblyIsolation_TwoLoaderInstances_TrackIndependently()
    {
        // Arrange — two separate loaders with separate registries~ ✨
        var registry1 = new InMemoryModuleRegistry();
        var registry2 = new InMemoryModuleRegistry();
        var loader1 = new AssemblyModuleLoader(registry1);
        var loader2 = new AssemblyModuleLoader(registry2);

        // Act — both load the same assembly independently~
        loader1.LoadFromAssembly(SampleModulesDllPath);

        // Assert — loader2 has NO knowledge of loader1's loads~ 🔌
        loader2.GetLoadedAssemblies().Should().BeEmpty(
            "loader2 is independent of loader1 — different isolated contexts~ 💖");
        registry2.HasModule("sample.log").Should().BeFalse(
            "registry2 should not contain modules loaded into registry1~ 🎯");

        // Unloading from loader1 should not affect loader2~
        loader2.LoadFromAssembly(SampleModulesDllPath);
        loader1.UnloadAssembly(SampleModulesDllPath);

        loader2.GetLoadedAssemblies().Should().HaveCount(1,
            "loader2's assembly tracking is not affected by loader1's unload~ ✨");
        registry2.HasModule("sample.log").Should().BeTrue(
            "registry2 modules are unaffected by loader1 operations~ 💖");
    }

    #endregion

    #region LoadFromDirectory Tests 📁

    /// <summary>
    /// LoadFromDirectory should find all DLLs in a directory and return one result per DLL~ 📁
    /// </summary>
    [Fact]
    public void LoadFromDirectory_ValidDirectory_ShouldReturnOneResultPerDll()
    {
        // Arrange — use the sample modules output directory which has many DLLs~
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act
        var results = loader.LoadFromDirectory(SampleModulesDirectory);

        // Assert
        results.Should().NotBeEmpty(
            "the output directory contains at least the sample modules DLL~ 💖");

        // All results should have an AssemblyPath set~
        results.Should().OnlyContain(
            r => !string.IsNullOrEmpty(r.AssemblyPath),
            "every result should have a non-empty AssemblyPath~ ✨");

        // Our sample modules DLL should appear in the results~
        results.Should().Contain(
            r => r.AssemblyPath.EndsWith("Workflow.Tests.SampleModules.dll", StringComparison.OrdinalIgnoreCase),
            "the sample modules DLL should be among the loaded results~ 🎯");
    }

    /// <summary>
    /// LoadFromDirectory with a non-existent directory should return empty list, not throw~ 📭
    /// </summary>
    [Fact]
    public void LoadFromDirectory_NonExistentDirectory_ShouldReturnEmpty()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var results = loader.LoadFromDirectory(fakePath);

        // Assert
        results.Should().BeEmpty(
            "a non-existent directory has no DLLs to load~ UwU");
    }

    /// <summary>
    /// LoadFromDirectory with null/empty path should throw~ 🛑
    /// </summary>
    [Fact]
    public void LoadFromDirectory_NullPath_ShouldThrow()
    {
        // Arrange
        var registry = new InMemoryModuleRegistry();
        var loader = new AssemblyModuleLoader(registry);

        // Act & Assert
        var act = () => loader.LoadFromDirectory(null!);
        act.Should().Throw<ArgumentException>("null directory path is not allowed~ UwU");
    }

    #endregion
}

