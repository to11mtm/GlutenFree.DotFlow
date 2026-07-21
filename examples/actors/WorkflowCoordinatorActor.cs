/// <summary>
}
    // Handles: CreateWorkflow, PauseWorkflow, ResumeWorkflow, CancelWorkflow
    
    private readonly IModuleRegistry _moduleRegistry;
    private readonly Dictionary<Guid, IActorRef> _activeWorkflows;
{
public class WorkflowCoordinatorActor : ReceiveActor
/// </remarks>
/// It handles workflow creation, scheduling, and cleanup.
/// CopilotNote: This actor is the parent supervisor for all workflow instances.
/// <remarks>
/// </summary>
/// Coordinates all workflow instances and their lifecycle.
/// 🌟 The heart of our workflow system, uwu~!

