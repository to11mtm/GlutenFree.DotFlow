/// <summary>
/// 🚀 Request to execute a workflow
/// </summary>
public record ExecuteWorkflowRequest
{
    /// <summary>
    /// Input data for the workflow 📥
    /// </summary>
    public Dictionary<string, object?>? Inputs { get; init; }
    
    /// <summary>
    /// Override workflow variables for this execution 🔧
    /// </summary>
    public Dictionary<string, object?>? Variables { get; init; }
    
    /// <summary>
    /// Wait for execution to complete (synchronous mode) ⏳
    /// </summary>
    public bool WaitForCompletion { get; init; } = false;
    
    /// <summary>
    /// Timeout for synchronous execution (seconds) ⏱️
    /// </summary>
    public int? TimeoutSeconds { get; init; }
    
    /// <summary>
    /// Correlation ID for tracking 🔍
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 📬 Response from workflow execution
/// </summary>
public record ExecutionResponse
{
    public Guid ExecutionId { get; init; }
    public ExecutionStatus Status { get; init; } = ExecutionStatus.Queued;
    public Dictionary<string, object?>? Outputs { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 📊 Detailed execution status
/// </summary>
public record ExecutionStatusDetail
{
    public Guid ExecutionId { get; init; }
    public Guid WorkflowId { get; init; }
    public string WorkflowName { get; init; } = string.Empty;
    public ExecutionStatus Status { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public TimeSpan? Duration { get; init; }
    public Dictionary<string, object?>? Inputs { get; init; }
    public Dictionary<string, object?>? Outputs { get; init; }
    public List<NodeExecutionStatus> NodeStatuses { get; init; } = [];
    public string? Error { get; init; }
    public string? CorrelationId { get; init; }
}

/// <summary>
/// 🧩 Individual node execution status
/// </summary>
public record NodeExecutionStatus
{
    public string NodeId { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public NodeStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public Dictionary<string, object?>? Outputs { get; init; }
    public string? Error { get; init; }
}

public enum ExecutionStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum NodeStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

