/// <summary>
/// ✨ Executes a single node in the workflow!
/// Wraps module execution with proper error handling and timeout.
/// </summary>
/// <remarks>
/// CopilotNote: This is an Akka.NET actor. It receives messages (ExecuteNode, CancelExecution, GetProgress)
/// and executes the configured IWorkflowModule. The configuration is immutable (set at creation),
/// while execution data is passed via messages.
/// </remarks>
public class NodeActor : ReceiveActor
{
    private readonly IWorkflowModule _module;
    private readonly NodeConfiguration _config;
    private readonly ILogger _logger;
    
    public NodeActor(NodeConfiguration config)
    {
        _config = config;
        _module = config.Module;
        _logger = config.LoggerFactory.CreateLogger($"NodeActor-{config.Definition.Id}");
        
        // Set up message handlers
        ReceiveAsync<ExecuteNode>(HandleExecute);
        ReceiveAsync<CancelExecution>(HandleCancel);
        ReceiveAsync<GetProgress>(HandleGetProgress);
    }
    
    private async Task HandleExecute(ExecuteNode message)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation(
                "Executing node {NodeId} for execution {ExecutionId}",
                _config.Definition.Id,
                message.ExecutionId);
            
            // Build the module execution context
            var context = new ModuleExecutionContext
            {
                Inputs = message.Inputs,
                Properties = _config.Definition.Properties,
                Variables = message.WorkflowVariables,
                Logger = _logger,
                Services = _config.ServiceProvider,
                ExecutionId = message.ExecutionId,
                NodeId = _config.Definition.Id
            };
            
            // Execute the module with timeout
            using var cts = new CancellationTokenSource(_config.Timeout);
            var result = await _module.ExecuteAsync(context, cts.Token);
            
            // Send result back to sender
            Sender.Tell(new NodeExecutionResult
            {
                ExecutionId = message.ExecutionId,
                NodeId = _config.Definition.Id,
                Success = result.Success,
                Outputs = result.Outputs,
                ErrorMessage = result.ErrorMessage,
                Exception = result.Exception,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Node execution failed for {NodeId}", _config.Definition.Id);
            
            Sender.Tell(new NodeExecutionResult
            {
                ExecutionId = message.ExecutionId,
                NodeId = _config.Definition.Id,
                Success = false,
                ErrorMessage = ex.Message,
                Exception = ex,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            });
        }
    }
    
    private async Task HandleCancel(CancelExecution message)
    {
        _logger.LogInformation("Cancelling execution {ExecutionId}", message.ExecutionId);
        // Implementation: Cancel the current execution
    }
    
    private async Task HandleGetProgress(GetProgress message)
    {
        // Implementation: Return current progress
    }
}

