namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What decided a CLI permission request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CliPermissionDecisionSourceEnum
    {
        /// <summary>
        /// A person decided it in Armada (dashboard, TUI, REST, WebSocket, or MCP).
        /// </summary>
        [EnumMember(Value = "Approver")]
        Approver,

        /// <summary>
        /// A remembered allow rule matched.
        /// </summary>
        [EnumMember(Value = "AllowRule")]
        AllowRule,

        /// <summary>
        /// A deny rule matched.
        /// </summary>
        [EnumMember(Value = "DenyRule")]
        DenyRule,

        /// <summary>
        /// No decision before the timeout.
        /// </summary>
        [EnumMember(Value = "Timeout")]
        Timeout,

        /// <summary>
        /// The session ended before a decision.
        /// </summary>
        [EnumMember(Value = "Cancelled")]
        Cancelled
    }
}
