namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The playbook snapshots one mission should have, written as part of a <see cref="MissionWriteBatch"/>.
    /// </summary>
    public class MissionSnapshotSet
    {
        /// <summary>
        /// Mission id.
        /// </summary>
        public string MissionId { get; set; }

        /// <summary>
        /// Snapshots in selection order.
        /// </summary>
        public List<MissionPlaybookSnapshot> Snapshots { get; set; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="snapshots">Snapshots in selection order.</param>
        /// <exception cref="ArgumentNullException">A value is null or empty.</exception>
        public MissionSnapshotSet(string missionId, List<MissionPlaybookSnapshot> snapshots)
        {
            if (String.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));
            MissionId = missionId;
            Snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        }
    }
}
