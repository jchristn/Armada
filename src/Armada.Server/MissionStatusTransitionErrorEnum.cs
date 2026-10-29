namespace Armada.Server
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a manual mission status change was refused. <see cref="None"/> means the change was stored.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionStatusTransitionErrorEnum
    {
        /// <summary>
        /// The status change was stored.
        /// </summary>
        None,

        /// <summary>
        /// The mission id does not exist in the caller's scope.
        /// </summary>
        NotFound,

        /// <summary>
        /// The status text is missing or is not a mission status.
        /// </summary>
        InvalidStatus,

        /// <summary>
        /// The current status cannot move to the requested status.
        /// </summary>
        InvalidTransition,

        /// <summary>
        /// The agent or its exit handoff still owns the mission, or landing could not be started or completed.
        /// </summary>
        Conflict
    }
}
