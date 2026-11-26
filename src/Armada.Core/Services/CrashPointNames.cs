namespace Armada.Core.Services
{
    /// <summary>
    /// Names of the <see cref="CrashPoints"/> placed between the writes of multi-step operations. Each name says which
    /// write has happened and which has not.
    /// </summary>
    public static class CrashPointNames
    {
        /// <summary>The Building voyage row exists; none of its missions do.</summary>
        public const string VoyageCreated = "voyage.created";

        /// <summary>Every mission of a Building voyage was written in one transaction; the voyage is not Open yet.</summary>
        public const string VoyageMissionCreated = "voyage.mission-created";

        /// <summary>
        /// After a voyage's missions are written and it moved from Building to Open, before the assignment pass.
        /// </summary>
        public const string VoyageOpened = "voyage.opened";

        /// <summary>A first-stage mission of an Open voyage was assigned (and possibly launched); every mission exists.</summary>
        public const string VoyageMissionAssigned = "voyage.mission-assigned";

        /// <summary>The agent process started; the captain's process id is not recorded yet.</summary>
        public const string LaunchBeforeProcessRecorded = "launch.before-process-recorded";

        /// <summary>A stage handoff updated one dependent stage; other dependents may not be updated yet.</summary>
        public const string HandoffDependentUpdated = "handoff.dependent-updated";

        /// <summary>The Architect's first Worker slot was filled; the other slices do not exist yet.</summary>
        public const string FanOutFirstSlice = "fanout.first-slice";

        /// <summary>An additional Architect slice was created; later slices may not exist yet.</summary>
        public const string FanOutSliceCreated = "fanout.slice-created";

        /// <summary>All slices and dependencies exist; the fan-out is not marked complete.</summary>
        public const string FanOutBeforeComplete = "fanout.before-complete";

        /// <summary>The merge queue pushed the verified commit; the entry is not marked Landed.</summary>
        public const string MergeQueuePushed = "mergequeue.pushed";

        /// <summary>The merge entry is Landed; the mission is not marked Complete.</summary>
        public const string MergeQueueEntryLanded = "mergequeue.entry-landed";

        /// <summary>A recovery incident was created; its failure kind is not recorded yet.</summary>
        public const string RecoveryIncidentCreated = "recovery.incident-created";

        /// <summary>A rescue mission was created; the incident does not reference it yet.</summary>
        public const string RecoveryRescueCreated = "recovery.rescue-created";
    }
}
