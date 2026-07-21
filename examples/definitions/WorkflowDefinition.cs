/// <summary>
/// 🌸 Represents a complete workflow definition
/// </summary>
public record WorkflowDefinition
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Version Version { get; init; }
    
    /// <summary>
    /// All nodes in this workflow 🧩
    /// </summary>
    public required IReadOnlyList<NodeDefinition> Nodes { get; init; }
    
    /// <summary>
    /// Connections between nodes 🔗
    /// </summary>
    public required IReadOnlyList<ConnectionDefinition> Connections { get; init; }
    
    /// <summary>
    /// Workflow-level variables 🔧
    /// </summary>
    public IReadOnlyDictionary<string, VariableDefinition> Variables { get; init; } = new Dictionary<string, VariableDefinition>();
    
    /// <summary>
    /// Trigger configuration (how the workflow starts) ⚡
    /// </summary>
    public TriggerDefinition? Trigger { get; init; }
    
    /// <summary>
    /// Error handling configuration 🛡️
    /// </summary>
    public ErrorHandlingConfig ErrorHandling { get; init; } = new();
}

/// <summary>
/// 🧩 A single node in the workflow
/// </summary>
public record NodeDefinition
{
    public required string Id { get; init; }
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    
    /// <summary>
    /// Configured property values ⚙️
    /// </summary>
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
    
    /// <summary>
    /// Position in the visual designer 📍
    /// </summary>
    public Position Position { get; init; } = new(0, 0);
    
    /// <summary>
    /// Node-specific error handling 🛡️
    /// </summary>
    public NodeErrorHandling? ErrorHandling { get; init; }
}

/// <summary>
/// 🔗 Connection between two nodes
/// </summary>
public record ConnectionDefinition
{
    public required string SourceNodeId { get; init; }
    public required string SourcePortName { get; init; }
    public required string TargetNodeId { get; init; }
    public required string TargetPortName { get; init; }
}

