namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// The live data of a work card: the tracked entity's status, progress, and per-mission (or per-target) detail.
    /// Built by the Ask work tracker; its hash detects changes.
    /// </summary>
    public class AskWorkSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Tracked work (atw_ prefix).
        /// </summary>
        public string TrackedWorkId
        {
            get => _TrackedWorkId;
            set => _TrackedWorkId = value ?? String.Empty;
        }

        /// <summary>
        /// Thread (ath_ prefix).
        /// </summary>
        public string ThreadId
        {
            get => _ThreadId;
            set => _ThreadId = value ?? String.Empty;
        }

        /// <summary>
        /// Tracked entity type.
        /// </summary>
        public AskTrackedEntityTypeEnum EntityType { get; set; } = AskTrackedEntityTypeEnum.Voyage;

        /// <summary>
        /// Tracked entity identifier.
        /// </summary>
        public string EntityId
        {
            get => _EntityId;
            set => _EntityId = value ?? String.Empty;
        }

        /// <summary>
        /// Entity title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? String.Empty;
        }

        /// <summary>
        /// Entity status string, or null when the entity no longer exists.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Coarse state derived from the status.
        /// </summary>
        public AskTrackedWorkStateEnum State { get; set; } = AskTrackedWorkStateEnum.Active;

        /// <summary>
        /// Whether the entity still exists.
        /// </summary>
        public bool Found { get; set; } = true;

        /// <summary>
        /// Number of missions, targets, or items.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Number finished successfully.
        /// </summary>
        public int Completed { get; set; } = 0;

        /// <summary>
        /// Number failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Number still running or waiting.
        /// </summary>
        public int Active { get; set; } = 0;

        /// <summary>
        /// Progress percentage 0-100.
        /// </summary>
        public int Progress
        {
            get => _Progress;
            set => _Progress = value < 0 ? 0 : (value > 100 ? 100 : value);
        }

        /// <summary>
        /// Counts keyed by status string.
        /// </summary>
        public Dictionary<string, int> CountsByStatus
        {
            get => _CountsByStatus;
            set => _CountsByStatus = value ?? new Dictionary<string, int>();
        }

        /// <summary>
        /// Per-mission rows (voyages, missions, and fleet action targets that produced voyages).
        /// </summary>
        public List<AskWorkMissionSnapshot> Missions
        {
            get => _Missions;
            set => _Missions = value ?? new List<AskWorkMissionSnapshot>();
        }

        /// <summary>
        /// Per-target rows (fleet action runs).
        /// </summary>
        public List<AskWorkTargetSnapshot> Targets
        {
            get => _Targets;
            set => _Targets = value ?? new List<AskWorkTargetSnapshot>();
        }

        /// <summary>
        /// Failure reason or error text, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// UTC start time, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC completion time, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _TrackedWorkId = String.Empty;
        private string _ThreadId = String.Empty;
        private string _EntityId = String.Empty;
        private string _Title = String.Empty;
        private int _Progress = 0;
        private Dictionary<string, int> _CountsByStatus = new Dictionary<string, int>();
        private List<AskWorkMissionSnapshot> _Missions = new List<AskWorkMissionSnapshot>();
        private List<AskWorkTargetSnapshot> _Targets = new List<AskWorkTargetSnapshot>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkSnapshot()
        {
        }

        #endregion
    }
}
