/// <summary>
/// 📜 Execute custom scripts in various languages!
/// Perfect for quick transformations and custom logic, uwu~
/// </summary>
[WorkflowModule("builtin.script")]
public class ScriptModule : IWorkflowModule
{
    public string ModuleId => "builtin.script";
    public string DisplayName => "Script";
    public string Category => "Advanced";
    public string Description => "Execute custom scripts in JavaScript, Lua, or Python";
    public string Icon => "📜";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "input", DisplayName = "Input Data", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "output", DisplayName = "Output Data", DataType = typeof(object) },
            new() { Name = "logs", DisplayName = "Script Logs", DataType = typeof(List<string>) }
        ],
        Properties =
        [
            new() { Name = "language", DisplayName = "Language", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, 
                    AllowedValues = ["JavaScript", "Lua", "Python"], 
                    DefaultValue = "JavaScript" },
            new() { Name = "script", DisplayName = "Script Code", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.Code },
            new() { Name = "timeout", DisplayName = "Timeout (seconds)", DataType = typeof(int), DefaultValue = 30 }
        ]
    };
    
    // ExecuteAsync implementation with language-specific execution...
}

