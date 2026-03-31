namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

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
        /// Total number of active voyages.
        /// </summary>
        public int ActiveVoyages { get; set; } = 0;

        /// <summary>
        /// Missions grouped by status.
        /// </summary>
        public Dictionary<string, int> MissionsByStatus
        {
            get => _MissionsByStatus;
            set => _MissionsByStatus = value ?? new Dictionary<string, int>();
        }

        /// <summary>
        /// Active voyages with progress information.
        /// </summary>
        public List<VoyageProgress> Voyages
        {
            get => _Voyages;
            set => _Voyages = value ?? new List<VoyageProgress>();
        }

        /// <summary>
        /// Recent signals.
        /// </summary>
        public List<Signal> RecentSignals
        {
            get => _RecentSignals;
            set => _RecentSignals = value ?? new List<Signal>();
        }

        /// <summary>
        /// Timestamp of this status snapshot.
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private Dictionary<string, int> _MissionsByStatus = new Dictionary<string, int>();
        private List<VoyageProgress> _Voyages = new List<VoyageProgress>();
        private List<Signal> _RecentSignals = new List<Signal>();

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
