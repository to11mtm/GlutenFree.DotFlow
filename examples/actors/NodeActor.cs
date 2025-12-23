/// <summary>
/// ✨ Executes a single node in the workflow!
/// Wraps module execution with proper error handling and timeout.
/// </summary>
public class NodeActor : ReceiveActor
{
    private readonly IWorkflowModule _module;
    private readonly NodeConfiguration _config;
    
    // Handles: Execute, Cancel, GetProgress
}

