namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// An Architect fan-out computed in memory before anything is written: the voyage's missions as the fan-out leaves
    /// them, which of them are new or changed, their playbook snapshots, and the events to record once the rows are
    /// written. <see cref="ToBatch"/> turns it into one <see cref="MissionWriteBatch"/>.
    /// </summary>
    internal sealed class ArchitectFanOutWork
    {
        private readonly Dictionary<string, MissionStatusEnum> _StatusAsRead = new Dictionary<string, MissionStatusEnum>(StringComparer.Ordinal);
        private readonly HashSet<string> _CreatedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Mission> _Created = new List<Mission>();
        private readonly List<Mission> _Changed = new List<Mission>();
        private readonly HashSet<string> _ChangedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, MissionSnapshotSet> _Snapshots = new Dictionary<string, MissionSnapshotSet>(StringComparer.Ordinal);

        /// <summary>
        /// The voyage's missions, including the ones this fan-out creates.
        /// </summary>
        internal List<Mission> Missions { get; }

        /// <summary>
        /// Events to record after the rows are written.
        /// </summary>
        internal List<ArmadaEvent> Events { get; } = new List<ArmadaEvent>();

        /// <summary>
        /// Number of missions this fan-out creates.
        /// </summary>
        internal int CreatedCount => _Created.Count;

        /// <summary>
        /// Instantiate over the voyage's missions as read.
        /// </summary>
        /// <param name="voyageMissions">The voyage's missions.</param>
        internal ArchitectFanOutWork(List<Mission> voyageMissions)
        {
            Missions = voyageMissions ?? throw new ArgumentNullException(nameof(voyageMissions));
            foreach (Mission mission in voyageMissions) _StatusAsRead[mission.Id] = mission.Status;
        }

        /// <summary>
        /// Add a mission the fan-out creates.
        /// </summary>
        /// <param name="mission">The new mission.</param>
        internal void Create(Mission mission)
        {
            if (!_CreatedIds.Add(mission.Id)) return;
            _Created.Add(mission);
            Missions.Add(mission);
        }

        /// <summary>
        /// Note that an existing mission was changed. A mission this fan-out creates is written as created instead.
        /// </summary>
        /// <param name="mission">The changed mission.</param>
        internal void Changed(Mission mission)
        {
            mission.LastUpdateUtc = DateTime.UtcNow;
            if (_CreatedIds.Contains(mission.Id) || !_ChangedIds.Add(mission.Id)) return;
            _Changed.Add(mission);
        }

        /// <summary>
        /// Set a mission's playbook snapshots.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="snapshots">Snapshots in selection order.</param>
        internal void SetSnapshots(string missionId, List<MissionPlaybookSnapshot> snapshots)
        {
            _Snapshots[missionId] = new MissionSnapshotSet(missionId, snapshots);
        }

        /// <summary>
        /// The snapshots this fan-out already set for a mission, or null.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <returns>The snapshots, or null.</returns>
        internal List<MissionPlaybookSnapshot>? PendingSnapshots(string missionId)
        {
            return _Snapshots.TryGetValue(missionId, out MissionSnapshotSet? set) ? set.Snapshots : null;
        }

        /// <summary>
        /// The rows to write. Every changed mission is written only while its stored status is still the one read.
        /// </summary>
        /// <param name="extra">Further guarded writes (the Architect's own completion mark).</param>
        /// <returns>The batch.</returns>
        internal MissionWriteBatch ToBatch(params MissionGuardedWrite[] extra)
        {
            MissionWriteBatch batch = new MissionWriteBatch();
            batch.Created.AddRange(_Created);
            foreach (Mission mission in _Changed)
                batch.Updated.Add(new MissionGuardedWrite(mission, new[] { _StatusAsRead.TryGetValue(mission.Id, out MissionStatusEnum read) ? read : mission.Status }));
            batch.Updated.AddRange(extra.Where(w => !_ChangedIds.Contains(w.Mission.Id)));
            batch.Snapshots.AddRange(_Snapshots.Values);
            return batch;
        }
    }
}
