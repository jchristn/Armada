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
        /// Historical pre-v1.3 boolean vessel limit. New decisions use numeric voyage and mission limits.
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
        NoEligibleCaptain,

        /// <summary>
        /// The captain could not be started (for example its CLI is missing on the host the mission was placed on);
        /// assignment is retried after a backoff.
        /// </summary>
        LaunchFailing,

        /// <summary>
        /// A captain is free, but no host can take the mission's dock yet (for example Require Harbor is on and no
        /// connected Harbor has the capabilities it needs, or every capable Harbor is at capacity).
        /// </summary>
        PlacementWaiting,

        /// <summary>
        /// Another active mission is working on the branch this mission continues (for example, two slices that both
        /// build on one earlier slice). It waits so both do not check out one branch at once; the slices then stack on
        /// the branch in order.
        /// </summary>
        BranchInUse,

        /// <summary>The vessel has no free voyage admission slot.</summary>
        VesselVoyageLimit,

        /// <summary>The voyage has no free running mission slot.</summary>
        VoyageMissionLimit,

        /// <summary>The deployment has no free running mission slot.</summary>
        GlobalMissionLimit,

        /// <summary>Eligible captains have no free mission slots.</summary>
        CaptainConcurrencyLimit,
        /// <summary>All eligible runtime capacity is occupied.</summary>
        RuntimeMissionLimit,
        /// <summary>All eligible inference endpoint capacity is occupied.</summary>
        EndpointMissionLimit,
        /// <summary>An eligible endpoint is disabled, unhealthy or temporarily circuit-broken.</summary>
        EndpointUnavailable,
        /// <summary>The mission's hierarchy parent (its terminal pipeline stage) has not finished.</summary>
        ParentNotFinished
    }
}
