namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Kind of occurrence a push notification announces. A device receives a push only when its category is enabled
    /// on the device (and the device is active).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PushCategoryEnum
    {
        /// <summary>
        /// An Ask Armada action proposal is waiting for the thread owner's approval.
        /// </summary>
        [EnumMember(Value = "AskProposal")]
        AskProposal,

        /// <summary>
        /// A CLI captain's permission prompt is waiting for an approver.
        /// </summary>
        [EnumMember(Value = "CliPermission")]
        CliPermission,

        /// <summary>
        /// A mission is awaiting review.
        /// </summary>
        [EnumMember(Value = "MissionReview")]
        MissionReview,

        /// <summary>
        /// A deployment is awaiting approval.
        /// </summary>
        [EnumMember(Value = "DeploymentApproval")]
        DeploymentApproval,

        /// <summary>
        /// A mission failed.
        /// </summary>
        [EnumMember(Value = "MissionFailed")]
        MissionFailed,

        /// <summary>
        /// A mission's work could not be landed.
        /// </summary>
        [EnumMember(Value = "LandingFailed")]
        LandingFailed,

        /// <summary>
        /// A captain is stalled.
        /// </summary>
        [EnumMember(Value = "CaptainStalled")]
        CaptainStalled,

        /// <summary>
        /// A voyage finished (completed or failed).
        /// </summary>
        [EnumMember(Value = "VoyageFinished")]
        VoyageFinished
    }
}
