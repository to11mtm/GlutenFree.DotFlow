// ────────────────────────────────────────────────────────────────────────────────
// Copyright © GlutenFree 2025. Made with love by Ami-Chan! UwU 💖
// Licensed under the MIT License.
// ────────────────────────────────────────────────────────────────────────────────

using Akka.Actor;
using Akka.Configuration;
using MessagePack;
using MessagePack.Resolvers;

namespace Workflow.Engine.Serialization;

/// <summary>
/// Provides helper methods for configuring Akka.NET ActorSystem with
/// MessagePack2 serialization and custom resolvers for LanguageExt types.
/// </summary>
/// <remarks>
/// CopilotNote: The Akka.Serialization.MessagePack2 package uses HOCON config
/// for serializer bindings. For custom MessagePack options with our LanguageExt
/// resolvers, we expose the options here for direct MessagePack serialization
/// in tests and for any future custom serializer needs.
///
/// Usage for ActorSystem:
/// <code>
/// var config = ConfigurationFactory.ParseString(MsgPack2Setup.GetSerializationHocon());
/// var system = ActorSystem.Create("MySystem", config);
/// </code>
///
/// Usage for direct MessagePack serialization:
/// <code>
/// var bytes = MessagePackSerializer.Serialize(obj, MsgPack2Setup.WorkflowOptions);
/// </code>
/// </remarks>
public static class MsgPack2Setup
{
    /// <summary>
    /// Gets the MessagePackSerializerOptions configured with LanguageExt type support.
    /// Use this for direct MessagePack serialization outside of Akka.NET.
    /// </summary>
    /// <remarks>
    /// CopilotNote: The resolver chain is:
    /// 1. MyResolver - Generated resolver for workflow types with LanguageExt.
    /// 2. ContractlessStandardResolver - Handles object? and dynamic types.
    /// 3. StandardResolver - Handles primitives and common .NET types.
    /// </remarks>
    public static MessagePackSerializerOptions WorkflowOptions { get; } =
        MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                MyResolver.Instance,
                ContractlessStandardResolver.Instance,
                StandardResolver.Instance))
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>
    /// Gets the HOCON configuration string for Akka.NET serialization with MessagePack2.
    /// </summary>
    /// <returns>HOCON configuration string for Akka.NET serialization.</returns>
    public static string GetSerializationHocon()
    {
        return """
            akka {
                actor {
                    serializers {
                        messagepack = "Akka.Serialization.MessagePack.MsgPackSerializer, Akka.Serialization.MessagePack"
                    }

                    serialization-bindings {
                        # Bind all workflow messages to MessagePack 📦
                        "Workflow.Engine.Messages.IWorkflowMessage, Workflow.Engine" = messagepack

                        # Bind core model types used in messages 🎀
                        "Workflow.Core.Models.WorkflowDefinition, Workflow.Core" = messagepack
                        "Workflow.Core.Models.NodeDefinition, Workflow.Core" = messagepack
                        "Workflow.Core.Models.ConnectionDefinition, Workflow.Core" = messagepack
                        "Workflow.Core.Models.VariableDefinition, Workflow.Core" = messagepack
                        "Workflow.Core.Models.NodeExecutionState, Workflow.Core" = messagepack
                    }

                    serialization-settings {
                        messagepack {
                            # Enable LZ4 compression for better performance 🚀
                            enable-lz4-compression = true
                        }
                    }
                }
            }
            """;
    }

    /// <summary>
    /// Gets the HOCON configuration for testing without full serialization.
    /// </summary>
    /// <returns>HOCON configuration string for test environments.</returns>
    public static string GetTestHocon()
    {
        return """
            akka {
                actor {
                    provider = "Akka.Actor.LocalActorRefProvider, Akka"
                }
                log-dead-letters = off
                log-dead-letters-during-shutdown = off
            }
            """;
    }

    /// <summary>
    /// Creates an Akka.NET Config object with serialization settings.
    /// </summary>
    /// <returns>Config object for ActorSystem creation.</returns>
    public static Config CreateSerializationConfig()
    {
        return ConfigurationFactory.ParseString(GetSerializationHocon());
    }

    /// <summary>
    /// Creates an Akka.NET Config object for testing.
    /// </summary>
    /// <returns>Config object for test ActorSystem creation.</returns>
    public static Config CreateTestConfig()
    {
        return ConfigurationFactory.ParseString(GetTestHocon());
    }
}
