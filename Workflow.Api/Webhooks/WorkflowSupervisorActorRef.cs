// <copyright file="WorkflowSupervisorActorRef.cs" company="GlutenFree">
// Copyright (c) GlutenFree. All rights reserved.
// </copyright>

namespace Workflow.Api.Webhooks;

using Akka.Actor;

/// <summary>
///  Phase 2.3.9 — DI-friendly wrapper around the <c>WorkflowSupervisor</c> actor reference~ ✨.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton in DI so <see cref="ActorWorkflowLauncher"/> can receive it
/// without depending on the raw Akka <see cref="ActorSystem"/> type.
/// Using a wrapper rather than registering <c>IActorRef</c> directly avoids DI ambiguity when
/// an actor system contains many refs~
/// </para>
/// <para>
/// CopilotNote: In tests, override this singleton with a <c>TestProbe.Ref</c> to verify
/// messages sent by <see cref="ActorWorkflowLauncher"/> without spinning up the full engine~
/// </para>
/// </remarks>
/// <param name="actorRef">The supervisor actor reference.</param>
public sealed class WorkflowSupervisorActorRef(IActorRef actorRef)
{
    /// <summary>Gets the underlying Akka actor reference~ .</summary>
    public IActorRef ActorRef { get; } = actorRef ?? throw new ArgumentNullException(nameof(actorRef));
}
