// <copyright file="IActorLifecycleHooks.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Engine.Actors;

/// <summary>
/// Provides extensible callback hooks for actor lifecycle events~ 🌸✨
/// Register implementations via DI to inject custom behavior during
/// PreStart, PostStop, PreRestart, and PostRestart of workflow actors.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: This is the core extensibility point for actor lifecycle management!
/// Consumers can implement this interface to plug in custom behavior like:
/// - External resource initialization/cleanup
/// - Monitoring system notifications
/// - Custom logging/metrics pipelines
/// - State backup to external systems
/// - Health check registration/deregistration
/// </para>
/// <para>
/// Use <see cref="CompositeActorLifecycleHooks"/> to chain multiple hooks together,
/// or <see cref="NullActorLifecycleHooks"/> as a no-op default. UwU 💖
/// </para>
/// </remarks>
public interface IActorLifecycleHooks
{
    /// <summary>
    /// Called when an actor is starting for the first time (before any message is processed).
    /// Use this to perform custom initialization — e.g., register with external services,
    /// open connections, or set up monitoring~ 🌸
    /// </summary>
    /// <param name="context">Information about the actor that is starting.</param>
    void OnPreStart(ActorLifecycleContext context);

    /// <summary>
    /// Called when an actor is permanently stopping.
    /// Use this to perform custom cleanup — e.g., deregister from services,
    /// close connections, flush buffers, or release external resources~ 👋🧹
    /// </summary>
    /// <param name="context">Information about the actor that is stopping.</param>
    void OnPostStop(ActorLifecycleContext context);

    /// <summary>
    /// Called before an actor restarts due to a supervision directive.
    /// Use this to preserve external state, notify monitoring systems,
    /// or perform pre-restart cleanup~ 🔄
    /// </summary>
    /// <param name="context">Information about the actor that is restarting.</param>
    /// <param name="reason">The exception that caused the restart.</param>
    /// <param name="message">The message being processed when the failure occurred (may be null).</param>
    void OnPreRestart(ActorLifecycleContext context, Exception reason, object? message);

    /// <summary>
    /// Called after an actor restarts (after constructor re-runs).
    /// Use this to restore external state, re-register with services,
    /// or perform post-restart initialization~ 🌸✨
    /// </summary>
    /// <param name="context">Information about the actor that was restarted.</param>
    /// <param name="reason">The exception that caused the restart.</param>
    void OnPostRestart(ActorLifecycleContext context, Exception reason);
}

/// <summary>
/// Contextual information passed to <see cref="IActorLifecycleHooks"/> callbacks~ 📋✨
/// Contains everything a hook implementation needs to know about the actor.
/// </summary>
/// <remarks>
/// CopilotNote: This record is intentionally lightweight — just identity + services.
/// Hooks shouldn't need to reach into actor internals. If they need more info,
/// they can resolve it from the <see cref="Services"/> provider. UwU 💖
/// </remarks>
/// <param name="ActorPath">The full Akka.NET path of the actor (e.g., "akka://system/user/supervisor"). 📍</param>
/// <param name="ActorType">The CLR type name of the actor (e.g., "WorkflowSupervisor"). 🏷️</param>
/// <param name="Services">The DI service provider for resolving dependencies. 🔧</param>
public record ActorLifecycleContext(
    string ActorPath,
    string ActorType,
    IServiceProvider Services);

/// <summary>
/// No-op implementation of <see cref="IActorLifecycleHooks"/> — does absolutely nothing~ 🤷✨
/// This is the default when no hooks are registered in DI.
/// </summary>
/// <remarks>
/// CopilotNote: Used as the fallback when <c>serviceProvider.GetService&lt;IActorLifecycleHooks&gt;()</c>
/// returns null. All methods are empty, so there's zero overhead. Kawaii efficiency! 💖
/// </remarks>
public class NullActorLifecycleHooks : IActorLifecycleHooks
{
    /// <summary>
    /// Singleton instance to avoid unnecessary allocations~ ✨
    /// </summary>
    public static readonly NullActorLifecycleHooks Instance = new();

    /// <inheritdoc/>
    public void OnPreStart(ActorLifecycleContext context)
    {
    }

    /// <inheritdoc/>
    public void OnPostStop(ActorLifecycleContext context)
    {
    }

    /// <inheritdoc/>
    public void OnPreRestart(ActorLifecycleContext context, Exception reason, object? message)
    {
    }

    /// <inheritdoc/>
    public void OnPostRestart(ActorLifecycleContext context, Exception reason)
    {
    }
}

/// <summary>
/// Chains multiple <see cref="IActorLifecycleHooks"/> implementations together~ 🔗✨
/// Each lifecycle callback is forwarded to all inner hooks in registration order.
/// </summary>
/// <remarks>
/// <para>
/// CopilotNote: Use this when you need multiple hook behaviors simultaneously!
/// For example: one hook for metrics, another for health checks, a third for cleanup.
/// Register via DI:
/// <code>
/// services.AddSingleton&lt;IActorLifecycleHooks&gt;(sp =&gt;
///     new CompositeActorLifecycleHooks(
///         new MetricsLifecycleHooks(sp.GetRequiredService&lt;IMetrics&gt;()),
///         new HealthCheckLifecycleHooks(sp.GetRequiredService&lt;IHealthCheckService&gt;())));
/// </code>
/// UwU 💖
/// </para>
/// </remarks>
public class CompositeActorLifecycleHooks : IActorLifecycleHooks
{
    private readonly IActorLifecycleHooks[] _hooks;

    /// <summary>
    /// Creates a new composite that chains the given hooks~ 🔗
    /// </summary>
    /// <param name="hooks">The hooks to chain, invoked in array order.</param>
    public CompositeActorLifecycleHooks(params IActorLifecycleHooks[] hooks)
    {
        _hooks = hooks ?? Array.Empty<IActorLifecycleHooks>();
    }

    /// <inheritdoc/>
    public void OnPreStart(ActorLifecycleContext context)
    {
        foreach (var hook in _hooks)
        {
            hook.OnPreStart(context);
        }
    }

    /// <inheritdoc/>
    public void OnPostStop(ActorLifecycleContext context)
    {
        foreach (var hook in _hooks)
        {
            hook.OnPostStop(context);
        }
    }

    /// <inheritdoc/>
    public void OnPreRestart(ActorLifecycleContext context, Exception reason, object? message)
    {
        foreach (var hook in _hooks)
        {
            hook.OnPreRestart(context, reason, message);
        }
    }

    /// <inheritdoc/>
    public void OnPostRestart(ActorLifecycleContext context, Exception reason)
    {
        foreach (var hook in _hooks)
        {
            hook.OnPostRestart(context, reason);
        }
    }
}

