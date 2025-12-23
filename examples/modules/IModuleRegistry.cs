/// <summary>
/// 🔍 Handles discovering and loading modules from uploaded assemblies
/// </summary>
/// <remarks>
/// CopilotNote: Uses AssemblyLoadContext for isolation!
/// Each module package gets its own context for clean unloading.
/// </remarks>
public interface IModuleRegistry
{
    /// <summary>
    /// Get all registered modules 📋
    /// </summary>
    IReadOnlyList<ModuleInfo> GetAllModules();
    
    /// <summary>
    /// Get a specific module by ID 🔍
    /// </summary>
    IWorkflowModule? GetModule(string moduleId);
    
    /// <summary>
    /// Load modules from an assembly package 📦
    /// </summary>
    Task<ModuleLoadResult> LoadModulePackageAsync(
        Stream assemblyStream, 
        ModulePackageMetadata metadata);
    
    /// <summary>
    /// Unload a module package (for updates/removal) 🗑️
    /// </summary>
    Task<bool> UnloadModulePackageAsync(string packageId);
}

/// <summary>
/// 📦 Metadata for a module package
/// </summary>
public record ModulePackageMetadata
{
    public required string PackageId { get; init; }
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
}

