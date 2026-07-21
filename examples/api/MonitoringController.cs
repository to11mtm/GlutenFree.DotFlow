/// <summary>
/// 📊 Monitoring and metrics endpoints
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class MonitoringController : ControllerBase
{
    /// <summary>
    /// Get system health status 💚
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthStatus), 200)]
    public async Task<IActionResult> GetHealth();
    
    /// <summary>
    /// Get workflow engine metrics 📈
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(EngineMetrics), 200)]
    public async Task<IActionResult> GetMetrics();
    
    /// <summary>
    /// Get active executions 🔄
    /// </summary>
    [HttpGet("active-executions")]
    [ProducesResponseType(typeof(List<ExecutionSummary>), 200)]
    public async Task<IActionResult> GetActiveExecutions();
}

