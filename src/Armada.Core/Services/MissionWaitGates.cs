namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>Maps assignment blockers to the wait gates recorded in mission_wait_intervals.</summary>
    internal static class MissionWaitGates
    {
        /// <summary>The wait gate a blocker reason represents.</summary>
        internal static MissionWaitGateEnum FromBlocker(MissionAssignmentBlockerReasonEnum reason)
        {
            switch (reason)
            {
                case MissionAssignmentBlockerReasonEnum.DependencyNotFinished:
                case MissionAssignmentBlockerReasonEnum.DependencyHandoffPending:
                    return MissionWaitGateEnum.Dependency;
                case MissionAssignmentBlockerReasonEnum.ParentNotFinished:
                    return MissionWaitGateEnum.Parent;
                case MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers:
                    return MissionWaitGateEnum.IntegrationBarrier;
                case MissionAssignmentBlockerReasonEnum.VesselBroadScopeMissionActive:
                case MissionAssignmentBlockerReasonEnum.BroadScopeWaitingForVessel:
                    return MissionWaitGateEnum.BroadScope;
                case MissionAssignmentBlockerReasonEnum.BranchInUse:
                    return MissionWaitGateEnum.BranchLock;
                case MissionAssignmentBlockerReasonEnum.VesselVoyageLimit:
                    return MissionWaitGateEnum.VesselVoyageSlot;
                case MissionAssignmentBlockerReasonEnum.VoyageMissionLimit:
                    return MissionWaitGateEnum.VoyageMissionSlot;
                case MissionAssignmentBlockerReasonEnum.GlobalMissionLimit:
                    return MissionWaitGateEnum.GlobalSlot;
                case MissionAssignmentBlockerReasonEnum.NoCaptains:
                case MissionAssignmentBlockerReasonEnum.NoIdleCaptain:
                case MissionAssignmentBlockerReasonEnum.NoEligibleCaptain:
                case MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit:
                    return MissionWaitGateEnum.CaptainSlot;
                case MissionAssignmentBlockerReasonEnum.RuntimeMissionLimit:
                    return MissionWaitGateEnum.RuntimeSlot;
                case MissionAssignmentBlockerReasonEnum.EndpointMissionLimit:
                case MissionAssignmentBlockerReasonEnum.EndpointUnavailable:
                    return MissionWaitGateEnum.EndpointSlot;
                case MissionAssignmentBlockerReasonEnum.PlacementWaiting:
                    return MissionWaitGateEnum.Placement;
                case MissionAssignmentBlockerReasonEnum.LaunchFailing:
                    return MissionWaitGateEnum.LaunchBackoff;
                default:
                    return MissionWaitGateEnum.Other;
            }
        }

        /// <summary>True for gates that mean the mission is ready and waits only for capacity.</summary>
        internal static bool IsCapacity(MissionWaitGateEnum gate)
            => gate == MissionWaitGateEnum.VesselVoyageSlot || gate == MissionWaitGateEnum.VoyageMissionSlot
                || gate == MissionWaitGateEnum.BranchLock || gate == MissionWaitGateEnum.GlobalSlot
                || gate == MissionWaitGateEnum.CaptainSlot || gate == MissionWaitGateEnum.EndpointSlot
                || gate == MissionWaitGateEnum.RuntimeSlot || gate == MissionWaitGateEnum.Placement;
    }
}
