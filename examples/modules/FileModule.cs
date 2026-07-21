/// <summary>
/// 📁 Read and write files from the filesystem!
/// Supports text, JSON, CSV, and binary files.
/// </summary>
[WorkflowModule("builtin.file")]
public class FileModule : IWorkflowModule
{
    public string ModuleId => "builtin.file";
    public string DisplayName => "File Operations";
    public string Category => "I/O";
    public string Description => "Read and write files";
    public string Icon => "📁";
    
    public ModuleSchema Schema => new()
    {
        Inputs =
        [
            new() { Name = "content", DisplayName = "Content to Write", DataType = typeof(object), IsRequired = false }
        ],
        Outputs =
        [
            new() { Name = "content", DisplayName = "File Content", DataType = typeof(object) },
            new() { Name = "exists", DisplayName = "File Exists", DataType = typeof(bool) },
            new() { Name = "metadata", DisplayName = "File Metadata", DataType = typeof(FileMetadata) }
        ],
        Properties =
        [
            new() { Name = "path", DisplayName = "File Path", DataType = typeof(string), 
                    IsRequired = true, EditorType = PropertyEditorType.FilePath },
            new() { Name = "operation", DisplayName = "Operation", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Read", "Write", "Append", "Delete", "Exists", "Copy", "Move"] },
            new() { Name = "format", DisplayName = "Format", DataType = typeof(string), 
                    EditorType = PropertyEditorType.Dropdown, AllowedValues = ["Text", "Json", "Csv", "Binary"], DefaultValue = "Text" },
            new() { Name = "encoding", DisplayName = "Encoding", DataType = typeof(string), DefaultValue = "UTF-8" }
        ]
    };
    
    // ExecuteAsync implementation...
}

