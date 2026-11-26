namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Mission rows written together in one transaction: new missions, status-guarded full-row updates of existing ones,
    /// the playbook snapshots of either, and the handoff artifacts of stages whose work these rows hand on. The batch is written completely or not at all.
    /// </summary>
    public class MissionWriteBatch
    {
        /// <summary>
        /// Missions to create, in order.
        /// </summary>
        public List<Mission> Created { get; set; } = new List<Mission>();

        /// <summary>
        /// Existing missions to rewrite; each applies only while its stored status is still one it expects.
        /// </summary>
        public List<MissionGuardedWrite> Updated { get; set; } = new List<MissionGuardedWrite>();

        /// <summary>
        /// Playbook snapshots to set, each replacing the mission's existing snapshots. Written after the missions.
        /// </summary>
        public List<MissionSnapshotSet> Snapshots { get; set; } = new List<MissionSnapshotSet>();

        /// <summary>
        /// Handoff artifacts to store (RS-6.9): the completed stage's artifact is written in the same transaction that
        /// prepares the stages reading it, so a stage can never launch before its handoff exists.
        /// </summary>
        public List<HandoffArtifact> HandoffArtifacts { get; set; } = new List<HandoffArtifact>();

        /// <summary>
        /// True when the batch writes nothing.
        /// </summary>
        public bool IsEmpty => Created.Count == 0 && Updated.Count == 0 && Snapshots.Count == 0 && HandoffArtifacts.Count == 0;
    }
}
