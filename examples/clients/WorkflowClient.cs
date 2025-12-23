/// <summary>
// Console.WriteLine($"Status: {result.Status}, Duration: {result.Duration}");
// var result = await client.WaitForCompletionAsync(execution.ExecutionId);
// // Wait for completion
// 
// Console.WriteLine($"Execution started: {execution.ExecutionId}");
// 
// });
//     ["destination"] = "database"
//     ["source"] = "api",
// {
// var execution = await client.ExecuteWorkflowAsync("data-sync", new Dictionary<string, object?>
// // Async execution
// 
// var client = new WorkflowClient("https://workflow-engine.example.com", apiKey: "your-api-key");
// Usage example:

}
    }
        throw new OperationCanceledException();
        
        }
            await Task.Delay(pollInterval.Value, cancellationToken);
            
                return status;
            if (status.Status is ExecutionStatus.Completed or ExecutionStatus.Failed or ExecutionStatus.Cancelled)
            
            var status = await GetExecutionStatusAsync(executionId, cancellationToken);
        {
        while (!cancellationToken.IsCancellationRequested)
        
        pollInterval ??= TimeSpan.FromSeconds(2);
    {
        CancellationToken cancellationToken = default)
        TimeSpan? pollInterval = null,
        Guid executionId,
    public async Task<ExecutionStatusDetail> WaitForCompletionAsync(
    /// </summary>
    /// Wait for execution to complete 🎯
    /// <summary>
    
    }
        return await response.Content.ReadFromJsonAsync<ExecutionStatusDetail>(cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        
            cancellationToken);
            $"{_baseUrl}/api/v1/workflows/executions/{executionId}",
        var response = await _httpClient.GetAsync(
    {
        CancellationToken cancellationToken = default)
        Guid executionId,
    public async Task<ExecutionStatusDetail> GetExecutionStatusAsync(
    /// </summary>
    /// Get execution status 📊
    /// <summary>
    
    }
        return await response.Content.ReadFromJsonAsync<ExecutionResponse>(cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        
            cancellationToken);
            request,
            $"{_baseUrl}/api/v1/workflows/execute/{workflowName}",
        var response = await _httpClient.PostAsJsonAsync(
        
        };
            TimeoutSeconds = timeoutSeconds
            WaitForCompletion = true,
            Inputs = inputs,
        { 
        var request = new ExecuteWorkflowRequest 
    {
        CancellationToken cancellationToken = default)
        int timeoutSeconds = 300,
        Dictionary<string, object?>? inputs = null,
        string workflowName,
    public async Task<ExecutionResponse> ExecuteWorkflowSyncAsync(
    /// </summary>
    /// Execute and wait for completion (synchronous) ⏳
    /// <summary>
    
    }
        return await response.Content.ReadFromJsonAsync<ExecutionResponse>(cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();
        
            cancellationToken);
            request,
            $"{_baseUrl}/api/v1/workflows/execute/{workflowName}",
        var response = await _httpClient.PostAsJsonAsync(
        var request = new ExecuteWorkflowRequest { Inputs = inputs };
    {
        CancellationToken cancellationToken = default)
        Dictionary<string, object?>? inputs = null,
        string workflowName,
    public async Task<ExecutionResponse> ExecuteWorkflowAsync(
    /// </summary>
    /// Execute a workflow and get the execution ID ✨
    /// <summary>
    
    }
            _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        if (apiKey != null)
        _httpClient = new HttpClient();
        _baseUrl = baseUrl;
    {
    public WorkflowClient(string baseUrl, string? apiKey = null)
    
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
{
public class WorkflowClient
/// </summary>
/// 💎 Official C# client SDK for the workflow engine

