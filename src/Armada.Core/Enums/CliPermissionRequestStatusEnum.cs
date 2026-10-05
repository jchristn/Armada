namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of a CLI permission request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionRequestStatusEnum
    {
        /// <summary>
        /// Waiting for an approver.
        /// </summary>
        [EnumMember(Value = "Pending")]
        Pending,

        /// <summary>
        /// Allowed (by an approver or an allow rule); the tool ran with its original input.
        /// </summary>
        [EnumMember(Value = "Allowed")]
        Allowed,

        /// <summary>
        /// Denied (by an approver or a deny rule).
        /// </summary>
        [EnumMember(Value = "Denied")]
        Denied,

        /// <summary>
        /// Not decided before Permissions.PromptTimeoutSeconds elapsed; denied.
        /// </summary>
        [EnumMember(Value = "Expired")]
        Expired,

        /// <summary>
        /// The turn or mission ended (or the Admiral restarted) before a decision; denied.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
