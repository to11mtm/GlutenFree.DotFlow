/// <summary>
/// 🌐 Makes HTTP requests to external APIs!
/// Supports GET, POST, PUT, PATCH, DELETE with full configuration.
/// </summary>
[WorkflowModule("builtin.http")]
public class HttpModule : IWorkflowModule
{
    public string ModuleId => "builtin.http";
    public string DisplayName => "HTTP Request";
    public string Category => "Network";
    public string Description => "Make HTTP requests to external APIs";
    public string Icon => "🌐";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "body", DisplayName = "Request Body", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "response", DisplayName = "Response Body", DataType = typeof(object) },
            new() { Name = "statusCode", DisplayName = "Status Code", DataType = typeof(int) },
            new() { Name = "headers", DisplayName = "Response Headers", DataType = typeof(Dictionary<string, string>) }
        ],
        Properties =
        [
            new() { Name = "url", DisplayName = "URL", DataType = typeof(string), IsRequired = true, EditorType = PropertyEditorType.Text },
            new() { Name = "method", DisplayName = "Method", DataType = typeof(string), EditorType = PropertyEditorType.Dropdown, 
                    AllowedValues = ["GET", "POST", "PUT", "PATCH", "DELETE"], DefaultValue = "GET" },
            new() { Name = "headers", DisplayName = "Headers", DataType = typeof(Dictionary<string, string>), EditorType = PropertyEditorType.Json },
            new() { Name = "timeout", DisplayName = "Timeout (seconds)", DataType = typeof(int), DefaultValue = 30 },
            new() { Name = "authentication", DisplayName = "Authentication", DataType = typeof(AuthConfig), EditorType = PropertyEditorType.Json }
        ]
    };
    
    // ExecuteAsync implementation...
}

