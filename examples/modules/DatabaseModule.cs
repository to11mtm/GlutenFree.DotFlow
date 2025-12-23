/// <summary>
/// 🗄️ Interact with databases using SQL!
/// Supports SQL Server, PostgreSQL, MySQL, SQLite.
/// </summary>
[WorkflowModule("builtin.database")]
public class DatabaseModule : IWorkflowModule
{
    public string ModuleId => "builtin.database";
    public string DisplayName => "Database Query";
    public string Category => "Data";
    public string Description => "Execute SQL queries against databases";
    public string Icon => "🗄️";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "parameters", DisplayName = "Query Parameters", DataType = typeof(Dictionary<string, object>), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "results", DisplayName = "Query Results", DataType = typeof(List<Dictionary<string, object>>) },
            new() { Name = "rowsAffected", DisplayName = "Rows Affected", DataType = typeof(int) }
        ],
        Properties =
        [
            new() { Name = "connectionString", DisplayName = "Connection String", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.ConnectionString },
            new() { Name = "provider", DisplayName = "Database Provider", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["SqlServer", "PostgreSQL", "MySQL", "SQLite"] },
            new() { Name = "query", DisplayName = "SQL Query", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.Code },
            new() { Name = "queryType", DisplayName = "Query Type", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Query", "NonQuery", "Scalar"] }
        ]
    };
    
    // ExecuteAsync implementation...
}

