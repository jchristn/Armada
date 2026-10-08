namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a captain chat turn (direct chat, Ask Armada turn, narration) could not run, for errors a client can act on.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainChatErrorCodeEnum
    {
        /// <summary>
        /// requireHarborForLaunch is on and no eligible Harbor is connected to run the captain.
        /// </summary>
        [EnumMember(Value = "HarborRequired")]
        HarborRequired,

        /// <summary>
        /// The runtime's CLI is not installed on the Admiral host (and no Harbor ran the turn).
        /// </summary>
        [EnumMember(Value = "RuntimeNotInstalled")]
        RuntimeNotInstalled,

        /// <summary>
        /// The Harbor chosen for the turn could not start the captain (its reason is in the error message).
        /// </summary>
        [EnumMember(Value = "HarborLaunchFailed")]
        HarborLaunchFailed
    }
}
