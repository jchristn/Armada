namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a Pending mission has not been assigned to a captain yet.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionAssignmentBlockerReasonEnum
    {
        /// <summary>
        /// Nothing blocks the mission; it is assigned on the next dispatch cycle (or when a launch policy, such as a
        /// required Harbor connection, allows it).
        /// </summary>
        AwaitingDispatch,

        /// <summary>
        /// The mission has no vessel, or its vessel no longer exists.
        /// </summary>
        VesselMissing,

        /// <summary>
        /// The vessel's configuration prevents docks from being provisioned (LocalPath and WorkingDirectory are the
        /// same directory).
        /// </summary>
        VesselMisconfigured,

        /// <summary>
        /// The mission depends on another mission that has not finished.
        /// </summary>
        DependencyNotFinished,

        /// <summary>
        /// The mission's dependency finished, and the handoff to this pipeline stage is still being prepared.
        /// </summary>
        DependencyHandoffPending,

        /// <summary>
        /// The architect sequenced this mission after the voyage's other implementation missions, which are still running.
        /// </summary>
        WaitingForVoyageWorkers,

        /// <summary>
        /// A broad-scope mission is running on the vessel and holds it exclusively.
        /// </summary>
        VesselBroadScopeMissionActive,

        /// <summary>
        /// This mission is broad scope and waits until the vessel has no active missions.
        /// </summary>
        BroadScopeWaitingForVessel,

        /// <summary>
        /// The vessel runs one mission at a time (AllowConcurrentMissions is off) and another mission is active.
        /// </summary>
        VesselConcurrencyLimit,

        /// <summary>
        /// No captains exist.
        /// </summary>
        NoCaptains,

        /// <summary>
        /// Every captain is busy (working, planning, refining, quarantined, stalled, or stopping).
        /// </summary>
        NoIdleCaptain,

        /// <summary>
        /// Idle captains exist, but none may take this mission's persona or required tier.
        /// </summary>
        NoEligibleCaptain
    }
}
