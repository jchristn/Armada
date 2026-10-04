namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where an Ask Armada action proposal came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskProposalSourceEnum
    {
        /// <summary>
        /// Proposed by the thread's captain through a thread-scoped MCP tool call.
        /// </summary>
        [EnumMember(Value = "Captain")]
        Captain,

        /// <summary>
        /// Submitted by the user through a built-in quick action form.
        /// </summary>
        [EnumMember(Value = "QuickAction")]
        QuickAction
    }
}
