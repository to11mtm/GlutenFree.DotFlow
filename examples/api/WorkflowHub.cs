/// <summary>
/// 📡 Real-time updates hub for workflow execution monitoring
/// </summary>
/// <remarks>
/// CopilotNote: Use SignalR for live updates to connected clients!
/// Perfect for UI real-time monitoring~ ✨
/// </remarks>
public class WorkflowHub : Hub
{
    /// <summary>
    /// Subscribe to workflow execution updates 📻
    /// </summary>
    public async Task SubscribeToExecution(Guid executionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }
    
    /// <summary>
    /// Unsubscribe from execution updates 🔇
    /// </summary>
    public async Task UnsubscribeFromExecution(Guid executionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }
    
    /// <summary>
    /// Subscribe to all workflow executions 📻
    /// </summary>
    public async Task SubscribeToAllExecutions()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "all-executions");
    }
}

// Events sent to clients:
// - ExecutionStarted(ExecutionStartedEvent)
// - ExecutionCompleted(ExecutionCompletedEvent)
// - ExecutionFailed(ExecutionFailedEvent)
// - NodeStarted(NodeStartedEvent)
// - NodeCompleted(NodeCompletedEvent)
// - NodeFailed(NodeFailedEvent)
// - ExecutionProgress(ExecutionProgressEvent)

