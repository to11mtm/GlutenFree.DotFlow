/// <summary>
/// 📚 Reusable script library system
/// Allows creating shared functions across workflows! ✨
/// </summary>
public interface IScriptLibrary
{
    /// <summary>
    /// Register a script library 📦
    /// </summary>
    Task RegisterLibraryAsync(ScriptLibraryDefinition library);
    
    /// <summary>
    /// Get a registered library 🔍
    /// </summary>
    ScriptLibraryDefinition? GetLibrary(string libraryId);
    
    /// <summary>
    /// List all available libraries 📋
    /// </summary>
    IReadOnlyList<ScriptLibraryDefinition> GetAllLibraries();
}

/// <summary>
/// 📖 Script library definition
/// </summary>
public record ScriptLibraryDefinition
{
    public required string LibraryId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Language { get; init; }
    public required string Code { get; init; }
    public IReadOnlyList<string> ExportedFunctions { get; init; } = [];
}

