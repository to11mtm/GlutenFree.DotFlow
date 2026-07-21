/// <summary>
/// 🔌 Interface for workflow engine plugins
/// </summary>
public interface IWorkflowPlugin
{
    string PluginId { get; }
    string Name { get; }
    Version Version { get; }
    
    /// <summary>
    /// Called when the plugin is loaded 📦
    /// </summary>
    Task InitializeAsync(IWorkflowPluginContext context);
    
    /// <summary>
    /// Called when the plugin is unloaded 🗑️
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// 🎁 Context provided to plugins
/// </summary>
public interface IWorkflowPluginContext
{
    IModuleRegistry ModuleRegistry { get; }
    IServiceCollection Services { get; }
    IConfiguration Configuration { get; }
}

