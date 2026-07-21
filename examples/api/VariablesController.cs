/// <summary>
/// 🔧 Variables and secrets management
/// </summary>
[ApiController]
[Route("api/v1/workflows/{workflowId}/[controller]")]
public class VariablesController : ControllerBase
{
    /// <summary>
    /// Get all workflow variables 📋
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Dictionary<string, VariableValue>), 200)]
    public async Task<IActionResult> GetVariables(Guid workflowId);
    
    /// <summary>
    /// Set a workflow variable 💾
    /// </summary>
    [HttpPut("{name}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> SetVariable(
        Guid workflowId,
        string name,
        [FromBody] SetVariableRequest request);
    
    /// <summary>
    /// Delete a workflow variable 🗑️
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteVariable(Guid workflowId, string name);
}

