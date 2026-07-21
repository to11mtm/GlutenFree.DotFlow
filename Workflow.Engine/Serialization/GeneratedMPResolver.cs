using LanguageExt;
using MessagePack;

[assembly:MessagePackAssumedFormattableAttribute(typeof(HashMap<string, object>))]
[assembly:MessagePackAssumedFormattableAttribute(typeof(Arr<string>))]
[assembly:MessagePackAssumedFormattableAttribute(typeof(Option<string>))]
[assembly:MessagePackAssumedFormattableAttribute(typeof(Option<HashMap<string, object>>))]
[assembly:MessagePackAssumedFormattableAttribute(typeof(Option<DateTimeOffset>))]
/*
 *                  case 0: return new global::Workflow.Engine.Serialization.MyResolver.LanguageExt.ArrFormatter<string>();
                    case 1: return new global::Workflow.Engine.Serialization.MyResolver.LanguageExt.OptionFormatter<global::LanguageExt.HashMap<string, object>>();
                    case 2: return new global::Workflow.Engine.Serialization.MyResolver.LanguageExt.OptionFormatter<string>();
                    case 3: return new global::Workflow.Engine.Serialization.MyResolver.LanguageExt.OptionFormatter<global::System.DateTimeOffset>();
 */
namespace Workflow.Engine.Serialization;

/// <summary>
/// Generated MessagePack resolver for workflow types with LanguageExt collections.
/// Made public so it can be accessed from tests and other assemblies~ 💖.
/// </summary>
/// <remarks>
/// CopilotNote: This resolver is used by AkkaMessagePackSerializer and tests.
/// It handles LanguageExt types like HashMap, Option, and Arr with source generation.
/// </remarks>
[GeneratedMessagePackResolver(UseMapMode = true)]
public partial class MyResolver
{

}
