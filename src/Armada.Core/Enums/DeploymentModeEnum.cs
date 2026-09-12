namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the Admiral executes host operations (agent processes, git, worktrees).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeploymentModeEnum
    {
        /// <summary>
        /// Standalone: the Admiral performs host operations in-process on the same machine. The default,
        /// zero-configuration model.
        /// </summary>
        [EnumMember(Value = "Local")]
        Local,

        /// <summary>
        /// Split: the Admiral delegates host operations to one or more attached Harbor runners over the
        /// Harbor link. Used when the Admiral runs in a container or on a different machine than the agents.
        /// </summary>
        [EnumMember(Value = "Split")]
        Split
    }
}
