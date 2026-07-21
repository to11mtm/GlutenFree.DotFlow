/// <summary>
/// 🌸 Main API endpoints for workflow management and execution
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class WorkflowsController : ControllerBase
{
    // === Workflow Management === 📋
    
    /// <summary>
    /// Get all workflows 📚
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<WorkflowSummary>), 200)]
    public async Task<IActionResult> GetWorkflows([FromQuery] WorkflowFilter? filter = null);
    
    /// <summary>
    /// Get a specific workflow by ID 🔍
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WorkflowDefinition), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetWorkflow(Guid id);
    
    /// <summary>
    /// Create a new workflow ✨
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WorkflowDefinition), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> CreateWorkflow([FromBody] CreateWorkflowRequest request);
    
    /// <summary>
    /// Update an existing workflow ✏️
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(WorkflowDefinition), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateWorkflow(Guid id, [FromBody] UpdateWorkflowRequest request);
    
    /// <summary>
    /// Delete a workflow 🗑️
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteWorkflow(Guid id);
    
    // === Workflow Execution === ▶️
    
    /// <summary>
    /// Execute a workflow 🚀
    /// </summary>
    [HttpPost("{id}/execute")]
    [ProducesResponseType(typeof(ExecutionResponse), 202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExecuteWorkflow(
        Guid id, 
        [FromBody] ExecuteWorkflowRequest request);
    
    /// <summary>
    /// Execute a workflow by name 🎯
    /// </summary>
    [HttpPost("execute/{name}")]
    [ProducesResponseType(typeof(ExecutionResponse), 202)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExecuteWorkflowByName(
        string name,
        [FromBody] ExecuteWorkflowRequest request);
    
    /// <summary>
    /// Get execution status 📊
    /// </summary>
    [HttpGet("executions/{executionId}")]
    [ProducesResponseType(typeof(ExecutionStatus), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExecutionStatus(Guid executionId);
    
    /// <summary>
    /// Cancel a running execution ⏹️
    /// </summary>
    [HttpPost("executions/{executionId}/cancel")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> CancelExecution(Guid executionId);
    
    /// <summary>
    /// Pause a running execution ⏸️
    /// </summary>
    [HttpPost("executions/{executionId}/pause")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> PauseExecution(Guid executionId);
    
    /// <summary>
    /// Resume a paused execution ▶️
    /// </summary>
    [HttpPost("executions/{executionId}/resume")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ResumeExecution(Guid executionId);
    
    /// <summary>
    /// Get execution history 📜
    /// </summary>
    [HttpGet("{id}/executions")]
    [ProducesResponseType(typeof(PagedResult<ExecutionSummary>), 200)]
    public async Task<IActionResult> GetExecutionHistory(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50);
}

