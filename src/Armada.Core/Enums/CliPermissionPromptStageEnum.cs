namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a CLI permission prompt in this process is (see CliPermissionService.GetInFlightPrompts), so a prompt that
    /// stalls can be traced to the step it is stuck in.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionPromptStageEnum
    {
        /// <summary>
        /// Reading and matching the applicable rules.
        /// </summary>
        [EnumMember(Value = "EvaluatingRules")]
        EvaluatingRules,

        /// <summary>
        /// Storing the request.
        /// </summary>
        [EnumMember(Value = "Storing")]
        Storing,

        /// <summary>
        /// Posting the CliPermission card into the Ask thread (the card message, then the request's message id).
        /// </summary>
        [EnumMember(Value = "PostingCard")]
        PostingCard,

        /// <summary>
        /// Announcing the request to its approvers.
        /// </summary>
        [EnumMember(Value = "Announcing")]
        Announcing,

        /// <summary>
        /// Waiting for a decision, the expiry, or cancellation.
        /// </summary>
        [EnumMember(Value = "Waiting")]
        Waiting,

        /// <summary>
        /// Recording the expiry or cancellation and reading the final state.
        /// </summary>
        [EnumMember(Value = "Resolving")]
        Resolving
    }
}
