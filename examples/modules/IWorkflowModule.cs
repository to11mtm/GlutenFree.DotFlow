namespace Workflow.Modules.Abstractions;

/// <summary>
/// 🌸 The base interface all workflow modules must implement!
/// This is the contract for creating new modules, uwu~
/// </summary>
/// <remarks>
/// CopilotNote: Module authors implement this interface to create
/// custom workflow nodes. Keep it simple and stateless!
/// </remarks>
public interface IWorkflowModule
{
    /// <summary>
    /// Unique identifier for this module type ✨
    /// </summary>
    string ModuleId { get; }
    
    /// <summary>
    /// Display name shown in the UI 🎨
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// Category for organizing in the module palette 📁
    /// </summary>
    string Category { get; }
    
    /// <summary>
    /// Description of what this module does 📝
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Icon identifier (emoji or icon name) 🖼️
    /// </summary>
    string Icon { get; }
    
    /// <summary>
    /// Defines the input/output schema for this module 📋
    /// </summary>
    ModuleSchema Schema { get; }
    
    /// <summary>
    /// Execute the module's logic! This is where the magic happens~ ✨
    /// </summary>
    /// <param name="context">Execution context with inputs and services</param>
    /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
    /// <returns>The execution result with outputs</returns>
    Task<ModuleResult> ExecuteAsync(
        ModuleExecutionContext context, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 🎁 Defines the input/output schema for a module
/// </summary>
public record ModuleSchema
{
    /// <summary>
    /// Input port definitions 📥
    /// </summary>
    public IReadOnlyList<PortDefinition> Inputs { get; init; } = [];
    
    /// <summary>
    /// Output port definitions 📤
    /// </summary>
    public IReadOnlyList<PortDefinition> Outputs { get; init; } = [];
    
    /// <summary>
    /// Configuration properties for the module ⚙️
    /// </summary>
    public IReadOnlyList<PropertyDefinition> Properties { get; init; } = [];
}

/// <summary>
/// 🔌 Defines an input or output port
/// </summary>
public record PortDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required Type DataType { get; init; }
    public string? Description { get; init; }
    public bool IsRequired { get; init; } = true;
    public object? DefaultValue { get; init; }
}

/// <summary>
/// ⚙️ Defines a configurable property
/// </summary>
public record PropertyDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required Type DataType { get; init; }
    public PropertyEditorType EditorType { get; init; } = PropertyEditorType.Text;
    public string? Description { get; init; }
    public bool IsRequired { get; init; } = true;
    public object? DefaultValue { get; init; }
    public IReadOnlyList<object>? AllowedValues { get; init; }
}

/// <summary>
/// 🖊️ Types of property editors for the UI
/// </summary>
public enum PropertyEditorType
{
    Text,
    MultilineText,
    Number,
    Boolean,
    Dropdown,
    FilePath,
    DirectoryPath,
    ConnectionString,
    Expression,
    Json,
    Code
}

/// <summary>
/// 📦 Context provided to modules during execution
/// </summary>
public record ModuleExecutionContext
{
    /// <summary>
    /// Input values from connected ports 📥
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }
    
    /// <summary>
    /// Configured property values ⚙️
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }
    
    /// <summary>
    /// Workflow-level variables 🔧
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }
    
    /// <summary>
    /// Logger for this module execution 📝
    /// </summary>
    public required ILogger Logger { get; init; }
    
    /// <summary>
    /// Service provider for dependency injection 💉
    /// </summary>
    public required IServiceProvider Services { get; init; }
    
    /// <summary>
    /// Unique execution ID for tracing 🔍
    /// </summary>
    public required Guid ExecutionId { get; init; }
    
    /// <summary>
    /// Node instance ID within the workflow 🆔
    /// </summary>
    public required string NodeId { get; init; }
}

/// <summary>
/// 🎯 Result of module execution
/// </summary>
public record ModuleResult
{
    public bool Success { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    
    public static ModuleResult Ok(Dictionary<string, object?> outputs) 
        => new() { Success = true, Outputs = outputs };
    
    public static ModuleResult Fail(string message, Exception? ex = null) 
        => new() { Success = false, ErrorMessage = message, Exception = ex };
}

