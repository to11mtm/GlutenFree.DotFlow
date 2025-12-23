/// <summary>
/// 🎀 Represents a single running workflow instance!
/// Each workflow gets its own actor for isolation and fault tolerance.
/// </summary>
public class WorkflowInstanceActor : ReceiveActor
{
    private WorkflowState _state;
    private readonly WorkflowDefinition _definition;
    private readonly Dictionary<string, IActorRef> _nodeActors;
    
    // Handles: StartExecution, NodeCompleted, NodeFailed, GetStatus
}

