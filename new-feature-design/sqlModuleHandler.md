```csharp
// ============================================================================
// WORKFLOW RUNTIME & COMPILATION ENGINE FOR LINQ2DB RUNTIME EVALUATION
// ============================================================================
// This single-file snippet outlines the architectural design for a pluggable
// workflow engine worker. It handles type registry, dynamic generation of a
// strongly-typed DataContext based on UI selections, Roslyn compilation with
// error diagnostics, and a SQLite preview runner.
//
// Dependencies required in your project:
// - Microsoft.CodeAnalysis.CSharp.Scripting (Roslyn)
// - linq2db
// - linq2db.SQLite (for preview/validation testing)
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using LinqToDB;
using LinqToDB.Data;

namespace WorkflowEngine.Core
{
#region 1. Metadata Models & Interfaces

    /// <summary>
    /// Represents a table available in the global catalog that engineers can select via the UI.
    /// </summary>
    public class WorkflowTableMetadata
    {
        public string TableName { get; set; }       // e.g., "Orders"
        public string ClrTypeName { get; set; }     // e.g., "PluginNamespace.Models.Order"
        public Assembly Assembly { get; set; }      // The loaded assembly containing the model type
    }

    /// <summary>
    /// Result payload returned to the UI after running compilation check.
    /// </summary>
    public class ValidationResult
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    #endregion

    #region 2. Code Generation & Compiler Engine

    public class WorkflowCompiler
    {
        /// <summary>
        /// Combines the selected tables and the engineer's raw C# code into a compiled in-memory assembly.
        /// </summary>
        public (CSharpCompilation Compilation, ValidationResult Result) CompileWorkflow(string userCode, IEnumerable<WorkflowTableMetadata> selectedTables)
        {
            var result = new ValidationResult();
            
            // 1. Generate the custom DataContext wrapper containing selected tables
            string contextCode = GenerateDynamicContextCode(selectedTables);
            
            // 2. Wrap the user code inside an execution class template
            string wrapperCode = WrapUserCode(userCode);

            var syntaxTrees = new[]
            {
                CSharpSyntaxTree.ParseText(contextCode),
                CSharpSyntaxTree.ParseText(wrapperCode)
            };

            // 3. Collect necessary metadata references for compilation
            var references = new HashSet<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IQueryable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DataConnection).Assembly.Location), // Linq2Db
                MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
                MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Expressions").Location)
            };

            // Include references from the plugin assemblies housing the selected models
            foreach (var table in selectedTables)
            {
                references.Add(MetadataReference.CreateFromFile(table.Assembly.Location));
            }

            var compilation = CSharpCompilation.Create(
                $"Workflow_Assembly_{Guid.NewGuid():N}",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            // 4. Evaluate diagnostics for errors
            var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (diagnostics.Any())
            {
                result.Success = false;
                result.Errors = diagnostics.Select(d => $"{d.Id}: {d.GetMessage()} at {d.Location.GetLineSpan()}").ToList();
            }
            else
            {
                result.Success = true;
            }

            return (compilation, result);
        }

        private string GenerateDynamicContextCode(IEnumerable<WorkflowTableMetadata> selectedTables)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using LinqToDB;");
            sb.AppendLine("using LinqToDB.Data;");
            sb.AppendLine();
            sb.AppendLine("public class DynamicWorkflowContext : LinqToDB.Data.DataConnection {");
            sb.AppendLine("    public DynamicWorkflowContext(string providerName, string connectionString) : base(providerName, connectionString) {}");
            
            foreach (var table in selectedTables)
            {
                // Generates strongly typed properties: public ITable<Namespace.Model> Orders => this.GetTable<Namespace.Model>();
                sb.AppendLine($"    public ITable<{table.ClrTypeName}> {table.TableName} => this.GetTable<{table.ClrTypeName}>();");
            }
            
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string WrapUserCode(string userCode)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using LinqToDB;");
            sb.AppendLine("using LinqToDB.Data;");
            sb.AppendLine();
            sb.AppendLine("namespace WorkflowRuntime {");
            sb.AppendLine("    public class WorkflowScript {");
            sb.AppendLine("        public async Task<object> ExecuteAsync(DynamicWorkflowContext db, dynamic payload) {");
            sb.AppendLine("            // --- USER CODE BEGINS ---");
            sb.AppendLine(userCode);
            sb.AppendLine("            // --- USER CODE ENDS ---");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }

    #endregion

    #region 3. Execution & SQLite Validation Preview Environment

    public class WorkflowExecutor
    {
        /// <summary>
        /// Executes a successfully compiled script against an isolated, short-lived memory stream 
        /// using an individual AssemblyLoadContext to prevent server memory bloat.
        /// </summary>
        public async Task<object> ExecutePreviewAsync(CSharpCompilation compilation, IEnumerable<WorkflowTableMetadata> selectedTables, object payload)
        {
            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                throw new InvalidOperationException("Compilation failed during assembly emission.");
            }

            ms.Seek(0, SeekOrigin.Begin);

            // Isolate the loaded script so it can be completely garbage collected later
            var alc = new AssemblyLoadContext($"WorkflowExecution_{Guid.NewGuid():N}", isCollectible: true);
            
            try
            {
                Assembly assembly = alc.LoadFromStream(ms);
                
                // Instantiate our generated dynamic context directed to an in-memory SQLite database
                // In production, your provider name string and connection string will vary
                string providerName = ProviderName.SQLite; 
                string connectionString = "Data Source=:memory:;Version=3;";

                var contextType = assembly.GetType("DynamicWorkflowContext");
                using var db = (DataConnection)Activator.CreateInstance(contextType, new object[] { providerName, connectionString });

                // Initialize tables in SQLite for testing/output preview validation
                db.Open();
                foreach (var table in selectedTables)
                {
                    // Dynamic generic invocation of DataExtensions.CreateTable<T>(db)
                    var createTableMethod = typeof(DataExtensions)
                        .GetMethods()
                        .First(m => m.Name == nameof(DataExtensions.CreateTable) && m.GetParameters().Length == 4);
                    
                    // Resolve the actual type from the plugin assembly
                    var targetType = table.Assembly.GetType(table.ClrTypeName) 
                                     ?? Type.GetType($"{table.ClrTypeName}, {table.Assembly.FullName}");
                    
                    var genericMethod = createTableMethod.MakeGenericMethod(targetType);
                    genericMethod.Invoke(null, new object[] { db, null, null, null });
                }

                // Instantiate the script wrapper and run it
                var scriptType = assembly.GetType("WorkflowRuntime.WorkflowScript");
                var scriptInstance = Activator.CreateInstance(scriptType);
                var executeMethod = scriptType.GetMethod("ExecuteAsync");

                // Invoke ExecuteAsync(db, payload)
                var task = (Task<object>)executeMethod.Invoke(scriptInstance, new object[] { db, payload });
                return await task;
            }
            finally
            {
                // Unload the context to free memory space immediately
                alc.Unload();
            }
        }
    }

    #endregion
}
```
