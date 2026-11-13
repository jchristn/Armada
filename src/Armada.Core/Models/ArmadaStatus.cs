namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Aggregate status summary across all active work.
    /// </summary>
    public class ArmadaStatus
    {
        #region Public-Members

        /// <summary>
        /// Total number of registered captains.
        /// </summary>
        public int TotalCaptains { get; set; } = 0;

        /// <summary>
        /// Number of idle captains.
        /// </summary>
        public int IdleCaptains { get; set; } = 0;

        /// <summary>
        /// Number of working captains.
        /// </summary>
        public int WorkingCaptains { get; set; } = 0;

        /// <summary>
        /// Number of stalled captains.
        /// </summary>
        public int StalledCaptains { get; set; } = 0;

        /// <summary>
        /// Total number of active (Open or InProgress) voyages. The voyages themselves, with progress, are read one page
        /// at a time from POST /api/v1/voyages/progress/enumerate (MCP list_voyage_progress).
        /// </summary>
        public int ActiveVoyages { get; set; } = 0;

        /// <summary>
        /// Number of InProgress voyages.
        /// </summary>
        public int InProgressVoyages { get; set; } = 0;

        /// <summary>
        /// Number of Open voyages (created, no mission assigned yet).
        /// </summary>
        public int OpenVoyages { get; set; } = 0;

        /// <summary>
        /// Number of signals in <see cref="RecentSignals"/> at most.
        /// </summary>
        public const int RecentSignalCount = 10;

        /// <summary>
        /// Cumulative number of dispatch attempts deferred because the host was under memory pressure
        /// (the resource-pressure admission gate declined to launch a captain).
        /// </summary>
        public long MemoryPressureDeferrals { get; set; } = 0;

        /// <summary>
        /// Missions grouped by status.
        /// </summary>
        [CollectionBound(VoyageProgress.MaxStatusEntries, Reason = "At most one entry per MissionStatusEnum value")]
        public Dictionary<string, int> MissionsByStatus
        {
            get => _MissionsByStatus;
            set => _MissionsByStatus = value ?? new Dictionary<string, int>();
        }

        /// <summary>
        /// Recent signals: the newest <see cref="RecentSignalCount"/>.
        /// </summary>
        [CollectionBound(RecentSignalCount, Reason = "The status snapshot reads the newest RecentSignalCount signals")]
        public List<Signal> RecentSignals
        {
            get => _RecentSignals;
            set => _RecentSignals = value ?? new List<Signal>();
        }

        /// <summary>
        /// Timestamp of this status snapshot.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Remote tunnel connectivity status.
        /// </summary>
        public RemoteTunnelStatus RemoteTunnel
        {
            get => _RemoteTunnel;
            set => _RemoteTunnel = value ?? new RemoteTunnelStatus();
        }

        #endregion

        #region Private-Members

        private Dictionary<string, int> _MissionsByStatus = new Dictionary<string, int>();
        private List<Signal> _RecentSignals = new List<Signal>();
        private RemoteTunnelStatus _RemoteTunnel = new RemoteTunnelStatus();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ArmadaStatus()
        {
        }

        #endregion
    }
}
