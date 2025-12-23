/// <summary>
/// 🌸 API exposed to scripts for workflow interaction
/// </summary>
/// <remarks>
/// CopilotNote: This is the bridge between scripts and the workflow engine!
/// Keep methods simple and well-documented for script authors.
/// </remarks>
public interface IWorkflowScriptApi
{
    // === Variable Management === 🔧
    
    /// <summary>
    /// Get a workflow variable value 📥
    /// </summary>
    object? GetVariable(string name);
    
    /// <summary>
    /// Set a workflow variable value 📤
    /// </summary>
    void SetVariable(string name, object? value);
    
    /// <summary>
    /// Check if a variable exists 🔍
    /// </summary>
    bool HasVariable(string name);
    
    // === Logging === 📝
    
    /// <summary>
    /// Log an informational message ℹ️
    /// </summary>
    void LogInfo(string message);
    
    /// <summary>
    /// Log a warning message ⚠️
    /// </summary>
    void LogWarning(string message);
    
    /// <summary>
    /// Log an error message ❌
    /// </summary>
    void LogError(string message);
    
    /// <summary>
    /// Log a debug message 🐛
    /// </summary>
    void LogDebug(string message);
    
    // === HTTP Operations === 🌐
    
    /// <summary>
    /// Make an HTTP GET request 🌐
    /// </summary>
    Task<HttpScriptResponse> HttpGetAsync(string url, Dictionary<string, string>? headers = null);
    
    /// <summary>
    /// Make an HTTP POST request 📤
    /// </summary>
    Task<HttpScriptResponse> HttpPostAsync(string url, object? body = null, Dictionary<string, string>? headers = null);
    
    /// <summary>
    /// Make an HTTP request with full control 🎯
    /// </summary>
    Task<HttpScriptResponse> HttpRequestAsync(HttpScriptRequest request);
    
    // === Data Operations === 💾
    
    /// <summary>
    /// Parse JSON string to object 📦
    /// </summary>
    object? ParseJson(string json);
    
    /// <summary>
    /// Convert object to JSON string 📄
    /// </summary>
    string ToJson(object? obj, bool pretty = false);
    
    /// <summary>
    /// Parse CSV string to table 📊
    /// </summary>
    List<Dictionary<string, object>> ParseCsv(string csv, bool hasHeaders = true);
    
    /// <summary>
    /// Convert table to CSV string 📋
    /// </summary>
    string ToCsv(List<Dictionary<string, object>> data, bool includeHeaders = true);
    
    // === Database Operations === 🗄️
    
    /// <summary>
    /// Execute a database query 🔍
    /// </summary>
    Task<List<Dictionary<string, object>>> QueryDatabaseAsync(string connectionString, string query, Dictionary<string, object>? parameters = null);
    
    /// <summary>
    /// Execute a database command (INSERT, UPDATE, DELETE) ✏️
    /// </summary>
    Task<int> ExecuteDatabaseAsync(string connectionString, string command, Dictionary<string, object>? parameters = null);
    
    // === File Operations === 📁
    
    /// <summary>
    /// Read text file content 📖
    /// </summary>
    Task<string> ReadFileAsync(string path);
    
    /// <summary>
    /// Write text to file 📝
    /// </summary>
    Task WriteFileAsync(string path, string content);
    
    /// <summary>
    /// Check if file exists 🔍
    /// </summary>
    bool FileExists(string path);
    
    /// <summary>
    /// List files in directory 📂
    /// </summary>
    List<string> ListFiles(string path, string pattern = "*");
    
    // === Utility Functions === 🛠️
    
    /// <summary>
    /// Sleep/delay execution ⏱️
    /// </summary>
    Task DelayAsync(int milliseconds);
    
    /// <summary>
    /// Generate a new GUID 🆔
    /// </summary>
    string NewGuid();
    
    /// <summary>
    /// Get current timestamp 📅
    /// </summary>
    DateTime Now();
    
    /// <summary>
    /// Format a date/time string 🕐
    /// </summary>
    string FormatDateTime(DateTime dateTime, string format);
    
    /// <summary>
    /// Encode string to Base64 🔐
    /// </summary>
    string Base64Encode(string text);
    
    /// <summary>
    /// Decode Base64 to string 🔓
    /// </summary>
    string Base64Decode(string base64);
    
    /// <summary>
    /// Hash string with specified algorithm 🔒
    /// </summary>
    string Hash(string text, string algorithm = "SHA256");
    
    // === Workflow Control === ⚡
    
    /// <summary>
    /// Trigger another workflow 🚀
    /// </summary>
    Task<Guid> TriggerWorkflowAsync(string workflowName, Dictionary<string, object>? inputs = null);
    
    /// <summary>
    /// Get the current execution context 🎯
    /// </summary>
    ScriptExecutionContext GetContext();
}

/// <summary>
/// 📦 HTTP request builder for scripts
/// </summary>
public record HttpScriptRequest
{
    public required string Url { get; init; }
    public string Method { get; init; } = "GET";
    public Dictionary<string, string>? Headers { get; init; }
    public object? Body { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// 📬 HTTP response for scripts
/// </summary>
public record HttpScriptResponse
{
    public int StatusCode { get; init; }
    public string Body { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new();
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}

/// <summary>
/// 🎯 Execution context available to scripts
/// </summary>
public record ScriptExecutionContext
{
    public Guid ExecutionId { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public string WorkflowName { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public Dictionary<string, object?> Inputs { get; init; } = new();
}

