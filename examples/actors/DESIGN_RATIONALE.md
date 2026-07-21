# 🎯 NodeActor Design Pattern Explained

Nya~ Great questions, senpai! Let me explain how the NodeActor design properly separates concerns! ✨

## 🤔 The Two Questions You Asked

### 1. Configuration vs. Messages: What Goes Where?

**BEFORE (Confused):**
```csharp
// ❌ BAD: Everything mixed in configuration
public record NodeConfiguration {
    public Guid ExecutionId { get; init; }  // Runtime data!
    public Dictionary<string, object?> InputValues { get; init; }  // Runtime data!
    public IWorkflowModule Module { get; init; }  // Static config!
}
```

**AFTER (Clear Separation):**
```csharp
// ✅ GOOD: Configuration = immutable, creation-time
public record NodeConfiguration {
    public NodeDefinition Definition { get; init; }  // Static: what node is this?
    public IWorkflowModule Module { get; init; }     // Static: what does it do?
    public TimeSpan Timeout { get; init; }          // Static: execution settings
    public ILoggerFactory LoggerFactory { get; init; }  // Static: infrastructure
}

// ✅ GOOD: Messages = runtime data per execution
public record ExecuteNode {
    public Guid ExecutionId { get; init; }           // Runtime: which workflow run?
    public Dictionary<string, object?> Inputs { get; init; }  // Runtime: input data
    public Dictionary<string, object?> Variables { get; init; }  // Runtime: workflow state
}
```

### 2. How Does the Node Know What To Do?

**The Module Reference!**

```csharp
public record NodeConfiguration {
    // ✨ This tells the node WHAT to do!
    public required IWorkflowModule Module { get; init; }
    
    // 🧩 This tells the node WHO it is and its settings
    public required NodeDefinition Definition { get; init; }
}
```

The `Module` property holds the actual implementation (HttpModule, DatabaseModule, etc.) that knows how to execute!

## 📦 Complete Flow Example

### 1. **Actor Creation** (Once per workflow node)

```csharp
// Load the module from registry
var httpModule = moduleRegistry.GetModule("builtin.http");

// Create configuration (immutable)
var config = new NodeConfiguration
{
    Definition = nodeDefinition,  // From workflow JSON: ID, properties, etc.
    Module = httpModule,           // The actual HTTP module instance
    Timeout = TimeSpan.FromMinutes(5),
    MaxRetries = 3,
    LoggerFactory = loggerFactory,
    ServiceProvider = serviceProvider
};

// Create the actor with this config
var nodeActor = actorSystem.ActorOf(
    Props.Create(() => new NodeActor(config)),
    $"node-{nodeDefinition.Id}"
);
```

### 2. **Execution** (Multiple times with different data)

```csharp
// First execution with data from upstream nodes
nodeActor.Tell(new ExecuteNode
{
    ExecutionId = Guid.NewGuid(),
    Inputs = new Dictionary<string, object?>
    {
        ["body"] = jsonPayload  // From previous node's output
    },
    WorkflowVariables = new Dictionary<string, object?>
    {
        ["apiKey"] = "secret-key"
    }
});

// Later... same actor, different execution!
nodeActor.Tell(new ExecuteNode
{
    ExecutionId = Guid.NewGuid(),  // Different execution ID
    Inputs = new Dictionary<string, object?>
    {
        ["body"] = anotherPayload  // Different input data
    },
    WorkflowVariables = currentVars  // Different variables
});
```

## 🎭 Why This Pattern?

### Configuration (Constructor/Props)
- ✅ **Immutable** - Set once, never changes
- ✅ **Actor identity** - Defines WHAT this actor is
- ✅ **Infrastructure** - Loggers, services, etc.
- ✅ **Policy** - Timeouts, retries, error handling

**Examples:**
- Which module to execute (HTTP, Database, File)
- Timeout duration
- Retry policy
- Logger factory
- Node definition (ID, name, properties)

### Messages (Tell/Ask)
- ✅ **Per-execution data** - Changes every time
- ✅ **Runtime state** - Current workflow variables
- ✅ **Input data** - From upstream nodes
- ✅ **Commands** - Execute, Cancel, GetProgress

**Examples:**
- Execution ID (each workflow run gets a new one)
- Input values from connected nodes
- Current workflow variables
- Cancellation requests

## 🔍 How the Actor Knows What To Do

```csharp
public class NodeActor : ReceiveActor
{
    private readonly IWorkflowModule _module;  // ← THIS tells us what to do!
    private readonly NodeConfiguration _config;
    
    public NodeActor(NodeConfiguration config)
    {
        _module = config.Module;  // Get the module from config
        
        // Set up message handlers
        Receive<ExecuteNode>(HandleExecute);
    }
    
    private async Task HandleExecute(ExecuteNode message)
    {
        // Build context from BOTH config and message
        var context = new ModuleExecutionContext
        {
            Inputs = message.Inputs,                    // From message (runtime)
            Properties = _config.Definition.Properties, // From config (static)
            Variables = message.WorkflowVariables,      // From message (runtime)
            Logger = _logger,                           // From config (static)
            Services = _config.ServiceProvider,         // From config (static)
            ExecutionId = message.ExecutionId,          // From message (runtime)
            NodeId = _config.Definition.Id              // From config (static)
        };
        
        // Execute the module!
        var result = await _module.ExecuteAsync(context, cancellationToken);
        
        // Reply with result
        Sender.Tell(new NodeExecutionResult { ... });
    }
}
```

## 🌟 Benefits of This Design

### 1. **Actor Reusability**
```csharp
// Same actor can handle multiple executions
nodeActor.Tell(new ExecuteNode { ExecutionId = run1, Inputs = data1 });
nodeActor.Tell(new ExecuteNode { ExecutionId = run2, Inputs = data2 });
nodeActor.Tell(new ExecuteNode { ExecutionId = run3, Inputs = data3 });
```

### 2. **Clear Separation**
- **What** the node does → `Module` in configuration
- **How** it should behave → Timeout, retries in configuration  
- **What data** to process → `Inputs` in message
- **Which execution** → `ExecutionId` in message

### 3. **Type Safety**
```csharp
// ✅ Compiler enforces correct message types
nodeActor.Tell(new ExecuteNode { ... });      // OK
nodeActor.Tell(new CancelExecution { ... });  // OK
nodeActor.Tell("execute");                    // Compile error!
```

### 4. **Testability**
```csharp
// Easy to test: mock the module
var mockModule = new Mock<IWorkflowModule>();
var config = new NodeConfiguration { Module = mockModule.Object, ... };
var actor = new NodeActor(config);

// Send test message
actor.Tell(new ExecuteNode { ExecutionId = testId, Inputs = testData });
```

## 📋 Summary

### NodeConfiguration (Answer to Question 2)
```csharp
public required IWorkflowModule Module { get; init; }  // ← Tells node WHAT to do
public required NodeDefinition Definition { get; init; } // ← Node identity & settings
```

### ExecuteNode Message (Answer to Question 1)
```csharp
// Runtime data that changes per execution
public Guid ExecutionId { get; init; }                 // Which workflow run
public Dictionary<string, object?> Inputs { get; init; } // Input data from upstream
public Dictionary<string, object?> Variables { get; init; } // Workflow state
```

## 🎀 Key Principle

> **Configuration = WHO the actor is and WHAT it can do**
> 
> **Messages = WHEN to do it and WITH WHAT data**

---

*Made with 💖 by Ami-Chan! This is the proper Akka.NET actor pattern, uwu~* 🌸

**The actor now has a clear identity (configuration) and responds to commands (messages)!** ✨

