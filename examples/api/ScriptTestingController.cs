/// <summary>
/// 🧪 Script testing endpoint for the UI
/// Allows testing scripts before adding them to workflows!
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ScriptTestingController : ControllerBase
{
    /// <summary>
    /// Test a script with sample data 🧪
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(ScriptTestResult), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> TestScript([FromBody] ScriptTestRequest request)
    {
        try
        {
            var executor = GetExecutor(request.Language);
            var api = new TestWorkflowScriptApi(); // Sandboxed API for testing
            
            var startTime = DateTime.UtcNow;
            var result = await executor.ExecuteAsync(
                request.Script,
                request.Inputs ?? new(),
                api,
                new ScriptExecutionConfig
                {
                    Timeout = TimeSpan.FromSeconds(30),
                    AllowNetwork = request.AllowNetwork,
                    AllowFileSystem = false,
                    AllowDatabase = false
                }
            );
            var duration = DateTime.UtcNow - startTime;
            
            return Ok(new ScriptTestResult
            {
                Success = true,
                Output = result,
                Logs = api.GetLogs(),
                Duration = duration,
                Variables = api.GetVariables()
            });
        }
        catch (Exception ex)
        {
            return Ok(new ScriptTestResult
            {
                Success = false,
                Error = ex.Message,
                Logs = new[] { ex.ToString() }
            });
        }
    }
}

/// <summary>
/// 🧪 Script test request
/// </summary>
public record ScriptTestRequest
{
    public required string Language { get; init; }
    public required string Script { get; init; }
    public Dictionary<string, object?>? Inputs { get; init; }
    public bool AllowNetwork { get; init; } = false;
}

/// <summary>
/// 📊 Script test result
/// </summary>
public record ScriptTestResult
{
    public bool Success { get; init; }
    public object? Output { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Logs { get; init; } = [];
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object?> Variables { get; init; } = new();
}

