namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of an Ask Armada action proposal.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AskProposalStatusEnum
    {
        /// <summary>
        /// Waiting for the user's decision.
        /// </summary>
        [EnumMember(Value = "Pending")]
        Pending,

        /// <summary>
        /// Approved and about to execute.
        /// </summary>
        [EnumMember(Value = "Approved")]
        Approved,

        /// <summary>
        /// Rejected by the user; never executed.
        /// </summary>
        [EnumMember(Value = "Rejected")]
        Rejected,

        /// <summary>
        /// Not decided within Ask.ProposalExpiryMinutes; never executed.
        /// </summary>
        [EnumMember(Value = "Expired")]
        Expired,

        /// <summary>
        /// Executed successfully.
        /// </summary>
        [EnumMember(Value = "Executed")]
        Executed,

        /// <summary>
        /// Executed and failed, or could not be executed.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
