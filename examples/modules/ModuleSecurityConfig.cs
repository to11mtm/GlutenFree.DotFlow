/// <summary>
/// 🛡️ Security configuration for module execution
/// </summary>
public record ModuleSecurityConfig
{
    /// <summary>
    /// Maximum execution time per node ⏱️
    /// </summary>
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Maximum memory per execution 💾
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 512 * 1024 * 1024; // 512 MB
    
    /// <summary>
    /// Allowed permissions for the module 🔐
    /// </summary>
    public ModulePermissions Permissions { get; init; } = ModulePermissions.None;
}

[Flags]
public enum ModulePermissions
{
    None = 0,
    Network = 1,
    FileSystem = 2,
    Database = 4,
    ProcessExecution = 8,
    EnvironmentVariables = 16,
    All = Network | FileSystem | Database | ProcessExecution | EnvironmentVariables
}

