using System.Text.Json.Serialization;

namespace Armada.Core.Enums
{
    /// <summary>
    /// Controls how a selected playbook is delivered to a mission.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PlaybookDeliveryModeEnum
    {
        /// <summary>
        /// Inline the full playbook markdown directly into the mission instructions.
        /// </summary>
        InlineFullContent = 0,

        /// <summary>
        /// Materialize the playbook outside the worktree and instruct the model to read it by path.
        /// </summary>
        InstructionWithReference = 1,

        /// <summary>
        /// Materialize the playbook inside the worktree and instruct the model to read it there.
        /// </summary>
        AttachIntoWorktree = 2
    }
}
