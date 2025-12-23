/// <summary>
/// 🎯 Configuration for script execution environments
/// </summary>
/// <remarks>
/// CopilotNote: Each language has different security and performance characteristics!
/// Configure appropriately for your use case.
/// </remarks>
public record ScriptExecutionConfig
{
    /// <summary>
    /// Maximum execution time ⏱️
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Maximum memory allocation 💾
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 256 * 1024 * 1024; // 256 MB
    
    /// <summary>
    /// Allow network access 🌐
    /// </summary>
    public bool AllowNetwork { get; init; } = true;
    
    /// <summary>
    /// Allow file system access 📁
    /// </summary>
    public bool AllowFileSystem { get; init; } = false;
    
    /// <summary>
    /// Allow database access 🗄️
    /// </summary>
    public bool AllowDatabase { get; init; } = true;
    
    /// <summary>
    /// Allowed file system paths (when filesystem access is enabled) 🔒
    /// </summary>
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
    
    /// <summary>
    /// Maximum number of HTTP requests per execution 🌐
    /// </summary>
    public int MaxHttpRequests { get; init; } = 10;
}

