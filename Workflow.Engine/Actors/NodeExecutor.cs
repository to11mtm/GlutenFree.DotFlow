// <copyright file="NodeExecutor.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.Core.Models;
using Workflow.Engine.Messages;
using Workflow.Modules.Abstractions;

/// <summary>
/// Actor responsible for executing a single workflow node by invoking modules. ✨
/// </summary>
/// <remarks>
/// <para>
/// The NodeExecutor is responsible for:
/// - Looking up the module from the registry
/// - Validating inputs against the module schema
/// - Creating the execution context
/// - Invoking the module's ExecuteAsync method
/// - Handling timeouts and cancellation
/// - Reporting results back to the parent WorkflowExecutor
/// </para>
/// <para>
/// CopilotNote: This actor bridges the Akka.NET actor world with the
/// async module execution world. We use PipeTo for async operations~ 💖
/// </para>
/// </remarks>
public class NodeExecutor : ReceiveActor
{
    private readonly ILoggingAdapter _log;
    private readonly string _nodeId;
    private readonly NodeDefinition _nodeDefinition;
    private readonly Dictionary<string, object?> _inputs;
    private readonly Guid _executionId;
    private readonly IServiceProvider _serviceProvider;
    private readonly Stopwatch _timer = new();
    private readonly IActorLifecycleHooks _lifecycleHooks;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isExecuting;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeExecutor"/> class.
    /// </summary>
    /// <param name="nodeId">The unique node ID.</param>
    /// <param name="nodeDefinition">The node definition.</param>
    /// <param name="inputs">Input values for the node.</param>
    /// <param name="executionId">The parent execution ID.</param>
    /// <param name="serviceProvider">Service provider for DI.</param>
    public NodeExecutor(
        string nodeId,
        NodeDefinition nodeDefinition,
        Dictionary<string, object?> inputs,
        Guid executionId,
        IServiceProvider serviceProvider)
    {
        _log = Context.GetLogger();
        _nodeId = nodeId;
        _nodeDefinition = nodeDefinition;
        _inputs = inputs;
        _executionId = executionId;
        _serviceProvider = serviceProvider;
        _lifecycleHooks = serviceProvider.GetService(typeof(IActorLifecycleHooks)) as IActorLifecycleHooks
            ?? NullActorLifecycleHooks.Instance;

        // Set up message handlers
        Receive<Execute>(HandleExecute);
        Receive<CancelExecution>(HandleCancel);
        Receive<ExecutionResult>(HandleExecutionResult);
        Receive<ReceiveTimeout>(HandleTimeout);

        _log.Debug("✨ NodeExecutor created for node {NodeId} (module: {ModuleId})", _nodeId, _nodeDefinition.ModuleId);
    }

    /// <summary>
    /// Creates Props for spawning a NodeExecutor actor.
    /// </summary>
    /// <param name="nodeId">The node ID.</param>
    /// <param name="nodeDefinition">The node definition.</param>
    /// <param name="inputs">Input values.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="serviceProvider">Service provider.</param>
    /// <returns>Props for actor creation.</returns>
    public static Props Props(
        string nodeId,
        NodeDefinition nodeDefinition,
        Dictionary<string, object?> inputs,
        Guid executionId,
        IServiceProvider serviceProvider)
    {
        return Akka.Actor.Props.Create(
            () => new NodeExecutor(nodeId, nodeDefinition, inputs, executionId, serviceProvider));
    }

    /// <summary>
    /// Handles the Execute message.
    /// Looks up the module, validates inputs, and invokes execution~ ⚡
    /// </summary>
    private void HandleExecute(Execute message)
    {
        if (_isExecuting)
        {
            _log.Warning("⚠️ Node {NodeId} is already executing, ignoring duplicate Execute message", _nodeId);
            return;
        }

        _isExecuting = true;
        _timer.Start();
        _cancellationTokenSource = new CancellationTokenSource();

        _log.Info("⚡ Executing node {NodeId} (module: {ModuleId})", _nodeId, _nodeDefinition.ModuleId);

        // Set receive timeout based on node configuration (Timeout is in milliseconds)
        var timeoutMs = _nodeDefinition.Timeout ?? 30000;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        Context.SetReceiveTimeout(timeout);

        try
        {
            // Look up the module
            var moduleRegistry = _serviceProvider.GetService<IModuleRegistry>();
            var module = moduleRegistry?.GetModule(_nodeDefinition.ModuleId);

            if (module == null)
            {
                // Module not found - use fallback stub behavior for testing
                _log.Warning(
                    "⚠️ Module '{ModuleId}' not found in registry, using fallback stub execution",
                    _nodeDefinition.ModuleId);
                ExecuteStubFallback();
                return;
            }

            _log.Debug("📦 Found module: {ModuleName} ({ModuleId})", module.DisplayName, module.ModuleId);

            // Validate inputs against module schema
            var validationErrors = ValidateInputs(module.Schema);
            if (validationErrors.Count > 0)
            {
                var errorMessage = $"Input validation failed: {string.Join(", ", validationErrors)}";
                _log.Error("❌ {Error}", errorMessage);
                SendFailure(new InvalidOperationException(errorMessage));
                return;
            }

            // Build execution context
            var context = BuildExecutionContext(module);

            // Execute the module asynchronously
            ExecuteModuleAsync(module, context, _cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "❌ Failed to start node execution: {Error}", ex.Message);
            SendFailure(ex);
        }
    }

    /// <summary>
    /// Executes the module asynchronously and pipes result back to self.
    /// </summary>
    private void ExecuteModuleAsync(IWorkflowModule module, ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        var self = Self;
        var parent = Context.Parent;

        Task.Run(async () =>
        {
            try
            {
                var result = await module.ExecuteAsync(context, cancellationToken);
                return new ExecutionResult(true, result, null);
            }
            catch (OperationCanceledException)
            {
                return new ExecutionResult(false, null, new OperationCanceledException("Node execution was cancelled"));
            }
            catch (Exception ex)
            {
                return new ExecutionResult(false, null, ex);
            }
        }, cancellationToken).PipeTo(self);
    }

    /// <summary>
    /// Handles the async execution result.
    /// </summary>
    private void HandleExecutionResult(ExecutionResult result)
    {
        Context.SetReceiveTimeout(null); // Clear timeout
        _timer.Stop();

        if (result.Success && result.ModuleResult != null)
        {
            if (result.ModuleResult.Success)
            {
                _log.Info(
                    "✅ Node {NodeId} completed successfully in {Duration}ms",
                    _nodeId,
                    _timer.ElapsedMilliseconds);

                SendSuccess(result.ModuleResult.Outputs.ToDictionary(kv => kv.Key, kv => kv.Value));
            }
            else
            {
                var error = result.ModuleResult.Exception
                    ?? new Exception(result.ModuleResult.ErrorMessage ?? "Module execution failed");
                _log.Error("❌ Node {NodeId} module returned failure: {Error}", _nodeId, result.ModuleResult.ErrorMessage);
                SendFailure(error);
            }
        }
        else
        {
            var error = result.Exception ?? new Exception("Unknown execution error");
            _log.Error(error, "❌ Node {NodeId} execution failed: {Error}", _nodeId, error.Message);
            SendFailure(error);
        }
    }

    /// <summary>
    /// Handles receive timeout (node took too long).
    /// </summary>
    private void HandleTimeout(ReceiveTimeout message)
    {
        _log.Error("⏰ Node {NodeId} timed out after {Duration}ms", _nodeId, _timer.ElapsedMilliseconds);
        _cancellationTokenSource?.Cancel();
        _timer.Stop();
        SendFailure(new TimeoutException($"Node {_nodeId} execution timed out"));
    }

    /// <summary>
    /// Handles cancellation request.
    /// </summary>
    private void HandleCancel(CancelExecution message)
    {
        _log.Info("🛑 Cancelling node {NodeId}", _nodeId);
        Context.SetReceiveTimeout(null);
        _cancellationTokenSource?.Cancel();
        _timer.Stop();
        // Actor will be stopped by parent
    }

    /// <summary>
    /// Validates inputs against the module schema.
    /// Checks for missing required inputs and validates data types~ 🔍✨
    /// </summary>
    /// <param name="schema">The module schema to validate against.</param>
    /// <returns>List of validation error messages.</returns>
    private List<string> ValidateInputs(ModuleSchema schema)
    {
        var errors = new List<string>();

        foreach (var inputDef in schema.Inputs)
        {
            var inputName = inputDef.Name;
            var inputValue = GetInputValue(inputName);

            // Check required inputs
            if (inputDef.IsRequired)
            {
                if (inputValue == null && inputDef.DefaultValue == null)
                {
                    errors.Add($"Required input '{inputName}' is missing");
                    continue;
                }
            }

            // Skip type validation for null values (optional inputs with no value)
            if (inputValue == null)
            {
                continue;
            }

            // Validate data type compatibility
            var typeError = ValidateDataType(inputName, inputValue, inputDef.DataType);
            if (typeError != null)
            {
                errors.Add(typeError);
            }
        }

        return errors;
    }

    /// <summary>
    /// Gets an input value by name, checking both exact match and fuzzy match.
    /// Supports "nodeName.portName" format for predecessor outputs~ 📥
    /// </summary>
    /// <param name="inputName">The input name to find.</param>
    /// <returns>The input value or null if not found.</returns>
    private object? GetInputValue(string inputName)
    {
        // Exact match
        if (_inputs.TryGetValue(inputName, out var value))
        {
            return value;
        }

        // Fuzzy match - check for case-insensitive or suffixed keys
        var matchingKey = _inputs.Keys.FirstOrDefault(k =>
            k.Equals(inputName, StringComparison.OrdinalIgnoreCase) ||
            k.EndsWith("." + inputName, StringComparison.OrdinalIgnoreCase));

        if (matchingKey != null)
        {
            return _inputs[matchingKey];
        }

        return null;
    }

    /// <summary>
    /// Validates that an input value is compatible with the expected data type.
    /// Returns an error message if validation fails, null if valid~ 🔍
    /// </summary>
    /// <param name="inputName">The name of the input for error messages.</param>
    /// <param name="value">The actual value.</param>
    /// <param name="expectedType">The expected data type.</param>
    /// <returns>Error message or null if valid.</returns>
    private static string? ValidateDataType(string inputName, object value, Type expectedType)
    {
        var actualType = value.GetType();

        // Exact type match
        if (expectedType.IsAssignableFrom(actualType))
        {
            return null;
        }

        // Allow object type to accept anything
        if (expectedType == typeof(object))
        {
            return null;
        }

        // Check for numeric conversions (int can become long, float can become double, etc.)
        if (IsNumericCompatible(actualType, expectedType))
        {
            return null;
        }

        // Check for string-to-primitive conversions
        if (expectedType == typeof(string))
        {
            return null; // Everything can be converted to string via ToString()
        }

        // Special handling for common type mismatches
        if (actualType == typeof(string) && CanParseFromString(expectedType))
        {
            return null; // Will be converted during execution
        }

        return $"Input '{inputName}' has type '{actualType.Name}' but expected '{expectedType.Name}'";
    }

    /// <summary>
    /// Checks if two numeric types are compatible for conversion.
    /// </summary>
    private static bool IsNumericCompatible(Type actual, Type expected)
    {
        var numericTypes = new System.Collections.Generic.HashSet<Type>
        {
            typeof(byte), typeof(sbyte),
            typeof(short), typeof(ushort),
            typeof(int), typeof(uint),
            typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
        };

        return numericTypes.Contains(actual) && numericTypes.Contains(expected);
    }

    /// <summary>
    /// Checks if a type can be parsed from a string representation.
    /// </summary>
    private static bool CanParseFromString(Type type)
    {
        return type == typeof(int) ||
               type == typeof(long) ||
               type == typeof(double) ||
               type == typeof(float) ||
               type == typeof(decimal) ||
               type == typeof(bool) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(Guid) ||
               type == typeof(TimeSpan);
    }

    /// <summary>
    /// Builds the module execution context.
    /// </summary>
    private ModuleExecutionContext BuildExecutionContext(IWorkflowModule module)
    {
        // Extract properties from node definition
        var properties = new Dictionary<string, object?>();
        foreach (var prop in _nodeDefinition.Properties)
        {
            properties[prop.Key] = ConvertJsonElement(prop.Value);
        }

        // Get logger from service provider or create null logger
        var loggerFactory = _serviceProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger($"Module.{module.ModuleId}.{_nodeId}")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        return new ModuleExecutionContext
        {
            Inputs = _inputs,
            Properties = properties,
            Variables = new Dictionary<string, object?>(), // TODO: Pass workflow variables
            Logger = logger,
            Services = _serviceProvider,
            ExecutionId = _executionId,
            NodeId = _nodeId,
        };
    }

    /// <summary>
    /// Converts a JsonElement to a regular .NET object.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString(),
        };
    }

    /// <summary>
    /// Fallback stub execution when module is not found.
    /// Allows the workflow engine to work even without registered modules~ 🧪
    /// </summary>
    private void ExecuteStubFallback()
    {
        _log.Info("🧪 Using stub execution fallback for node {NodeId}", _nodeId);

        var outputs = new Dictionary<string, object?>
        {
            ["result"] = $"Executed {_nodeId} (stub)",
            ["success"] = true,
            ["timestamp"] = DateTimeOffset.UtcNow,
            ["moduleId"] = _nodeDefinition.ModuleId,
        };

        // Copy inputs to outputs for data flow testing
        foreach (var (key, value) in _inputs)
        {
            outputs[$"input_{key}"] = value;
        }

        _timer.Stop();
        SendSuccess(outputs);
    }

    /// <summary>
    /// Sends success message to parent.
    /// </summary>
    private void SendSuccess(Dictionary<string, object?> outputs)
    {
        _isExecuting = false;
        Context.SetReceiveTimeout(null);

        Context.Parent.Tell(new NodeExecutionCompleted(
            _nodeId,
            outputs.ToHashMap(),
            _executionId,
            _timer.Elapsed));
    }

    /// <summary>
    /// Sends failure message to parent.
    /// </summary>
    private void SendFailure(Exception error)
    {
        _isExecuting = false;
        Context.SetReceiveTimeout(null);
        _timer.Stop();

        Context.Parent.Tell(new NodeExecutionFailed(
            _nodeId,
            error,
            _executionId,
            _timer.Elapsed));
    }

    /// <summary>
    /// Lifecycle hook called when the actor is starting for the first time.
    /// Logs initialization and validates the service provider state~ 🌸✨
    /// </summary>
    /// <remarks>
    /// CopilotNote: PreStart runs before any message is delivered! We use it
    /// to verify the DI container has what we need and log the node config. UwU 💖
    /// </remarks>
    protected override void PreStart()
    {
        base.PreStart();
        _log.Info(
            "🌸 NodeExecutor initializing for node {NodeId} (module: {ModuleId}, execution: {ExecutionId})",
            _nodeId,
            _nodeDefinition.ModuleId,
            _executionId);
        _lifecycleHooks.OnPreStart(CreateLifecycleContext());
    }

    /// <summary>
    /// Lifecycle hook called before the actor restarts due to supervision.
    /// Cancels any running execution and cleans up resources~ 🔄
    /// </summary>
    /// <remarks>
    /// <para>
    /// CopilotNote: When a NodeExecutor crashes mid-execution, we need to cancel any
    /// in-flight async work (module execution) to prevent dangling tasks. We dispose
    /// the CancellationTokenSource and reset the execution flag so PostRestart can
    /// set up a clean slate for a fresh retry. Kawaii cleanup! 💕
    /// </para>
    /// </remarks>
    /// <param name="reason">The exception that caused the restart.</param>
    /// <param name="message">The message being processed when the failure occurred.</param>
    protected override void PreRestart(Exception reason, object message)
    {
        _log.Warning(
            "🔄 NodeExecutor restarting for node {NodeId} due to: {Error}~ UwU",
            _nodeId,
            reason.Message);

        _lifecycleHooks.OnPreRestart(CreateLifecycleContext(), reason, message);

        // Cancel any in-flight execution to prevent dangling tasks~ 🛑
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        // Clear the receive timeout
        Context.SetReceiveTimeout(null);

        _timer.Stop();

        // Call base to allow Akka to stop children (NodeExecutor has no children, but it's good form)
        base.PreRestart(reason, message);
    }

    /// <summary>
    /// Lifecycle hook called after the actor restarts.
    /// Resets execution state for a clean retry~ 🌸✨
    /// </summary>
    /// <remarks>
    /// CopilotNote: After restart, the constructor runs again so most fields are fresh.
    /// We just reset <c>_isExecuting</c> in case state was partially set before the crash.
    /// The parent WorkflowExecutor will re-send <see cref="Execute"/> if it wants a retry! 💖
    /// </remarks>
    /// <param name="reason">The exception that caused the restart.</param>
    protected override void PostRestart(Exception reason)
    {
        base.PostRestart(reason);

        _isExecuting = false;
        _timer.Reset();

        _log.Info(
            "🌸 NodeExecutor restarted for node {NodeId}. Ready for fresh execution~ ✨",
            _nodeId);

        _lifecycleHooks.OnPostRestart(CreateLifecycleContext(), reason);
    }

    /// <summary>
    /// Lifecycle hook called when the actor is stopping.
    /// Cancels running execution, disposes resources, and logs final state~ 👋🧹
    /// </summary>
    /// <remarks>
    /// CopilotNote: PostStop is the last chance to release resources!
    /// We cancel the CTS, stop the timer, and log our farewell. Sayonara~ 💕
    /// </remarks>
    protected override void PostStop()
    {
        _timer.Stop();

        // Cancel and dispose the CTS if it's still alive~ 🛑
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        // Clear any pending receive timeout~ ⏰
        Context.SetReceiveTimeout(null);

        _log.Info(
            "👋 NodeExecutor stopping for node {NodeId} (was executing: {WasExecuting}, elapsed: {ElapsedMs}ms)",
            _nodeId,
            _isExecuting,
            _timer.ElapsedMilliseconds);

        _lifecycleHooks.OnPostStop(CreateLifecycleContext());
        base.PostStop();
    }

    /// <summary>
    /// Creates an <see cref="ActorLifecycleContext"/> for passing to lifecycle hooks~ 🌸
    /// </summary>
    /// <returns>A context describing this actor instance.</returns>
    private ActorLifecycleContext CreateLifecycleContext()
    {
        return new ActorLifecycleContext(
            ActorPath: Self.Path.ToString(),
            ActorType: nameof(NodeExecutor),
            Services: _serviceProvider);
    }

    /// <summary>
    /// Internal message for async execution result.
    /// </summary>
    private record ExecutionResult(bool Success, ModuleResult? ModuleResult, Exception? Exception);
}

