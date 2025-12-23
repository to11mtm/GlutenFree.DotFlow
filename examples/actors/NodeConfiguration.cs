/// <summary>
/// 🎯 Configuration for a NodeActor instance (creation-time, immutable)
/// Contains the static configuration needed to create and configure a node actor, uwu~
/// </summary>
/// <remarks>
/// CopilotNote: This is passed to NodeActor ONCE during actor creation (Props).
/// It contains IMMUTABLE configuration that doesn't change per execution.
/// Runtime execution data is passed via ExecuteNode message!
/// </remarks>
public record NodeConfiguration
{
    /// <summary>
    /// The node definition from the workflow 🧩
    /// Contains: NodeId, ModuleId, Name, Properties, Position, ErrorHandling
    /// </summary>
    public required NodeDefinition Definition { get; init; }
    
    /// <summary>
    /// The workflow module this node will execute ✨
    /// This tells the node WHAT to do (HTTP request, database query, etc.)
    /// </summary>
    public required IWorkflowModule Module { get; init; }
    
    /// <summary>
    /// Execution timeout for this node ⏱️
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// Maximum number of retry attempts on failure 🔄
    /// </summary>
    public int MaxRetries { get; init; } = 0;
    
    /// <summary>
    /// Delay between retry attempts ⏳
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Whether to continue workflow on failure 🛡️
    /// </summary>
    public bool ContinueOnError { get; init; } = false;
    
    /// <summary>
    /// Logger factory for creating node-specific loggers 📝
    /// </summary>
    public required ILoggerFactory LoggerFactory { get; init; }
    
    /// <summary>
    /// Service provider for dependency injection 💉
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }
}

// ============================================================================
// 📬 Actor Messages (runtime execution)
// ============================================================================

/// <summary>
/// 📬 Message to execute the node with specific input data
/// This is sent to the NodeActor each time it needs to execute!
/// </summary>
/// <remarks>
/// CopilotNote: This contains RUNTIME data that changes per execution.
/// The NodeActor receives this message, executes the module, and replies with NodeExecutionResult.
/// </remarks>
public record ExecuteNode
{
    /// <summary>
    /// Unique execution ID for this workflow run 🔍
    /// </summary>
    public required Guid ExecutionId { get; init; }
    
    /// <summary>
    /// Input values from connected nodes' output ports 📥
    /// Key = port name, Value = data from upstream node
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Inputs { get; init; }
    
    /// <summary>
    /// Workflow-level variables for this execution 🔧
    /// These can change between workflow runs
    /// </summary>
    public required IReadOnlyDictionary<string, object?> WorkflowVariables { get; init; }
}

/// <summary>
/// 📬 Response from node execution
/// </summary>
public record NodeExecutionResult
{
    public required Guid ExecutionId { get; init; }
    public required string NodeId { get; init; }
    public required bool Success { get; init; }
    public IReadOnlyDictionary<string, object?> Outputs { get; init; } = new Dictionary<string, object?>();
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime CompletedAt { get; init; }
}

/// <summary>
/// 📬 Message to cancel execution
/// </summary>
public record CancelExecution
{
    public required Guid ExecutionId { get; init; }
}

/// <summary>
/// 📬 Message to get current execution progress
/// </summary>
public record GetProgress
{
    public required Guid ExecutionId { get; init; }
}

