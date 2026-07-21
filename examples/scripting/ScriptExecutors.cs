/// <summary>
/// 🟨 JavaScript executor using Jint
/// </summary>
public class JavaScriptExecutor : IScriptExecutor
{
    public string Language => "JavaScript";
    
    public async Task<object?> ExecuteAsync(
        string script, 
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(config.Timeout);
            options.LimitMemory(config.MaxMemoryBytes);
        });
        
        // Inject API
        engine.SetValue("api", api);
        engine.SetValue("$input", inputs.GetValueOrDefault("input"));
        
        // Execute script
        var result = engine.Evaluate(script);
        return result.ToObject();
    }
}

/// <summary>
/// 🌙 Lua executor using MoonSharp
/// </summary>
public class LuaExecutor : IScriptExecutor
{
    public string Language => "Lua";
    
    public async Task<object?> ExecuteAsync(
        string script,
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var luaScript = new Script();
        
        // Register API
        UserData.RegisterType<IWorkflowScriptApi>();
        luaScript.Globals["workflow"] = new
        {
            api = api,
            input = inputs.GetValueOrDefault("input")
        };
        
        // Execute with timeout
        using var cts = new CancellationTokenSource(config.Timeout);
        var result = luaScript.DoString(script);
        
        return result.ToObject();
    }
}

/// <summary>
/// 🐍 Python executor using IronPython or Python.NET
/// </summary>
public class PythonExecutor : IScriptExecutor
{
    public string Language => "Python";
    
    public async Task<object?> ExecuteAsync(
        string script,
        Dictionary<string, object?> inputs,
        IWorkflowScriptApi api,
        ScriptExecutionConfig config)
    {
        var engine = Python.CreateEngine();
        var scope = engine.CreateScope();
        
        // Inject API and input
        scope.SetVariable("workflow", new
        {
            api = api,
            input = inputs.GetValueOrDefault("input")
        });
        
        // Execute with timeout
        using var cts = new CancellationTokenSource(config.Timeout);
        var result = engine.Execute(script, scope);
        
        return result;
    }
}

