namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Progress information for a voyage: the voyage and counts of its missions. The missions themselves are not
    /// embedded; read them one page at a time with the missions enumeration filtered by voyageId.
    /// </summary>
    public class VoyageProgress
    {
        #region Public-Members

        /// <summary>
        /// Voyage details.
        /// </summary>
        public Voyage Voyage { get; set; } = null!;

        /// <summary>
        /// Total missions in this voyage.
        /// </summary>
        public int TotalMissions { get; set; } = 0;

        /// <summary>
        /// Completed missions in this voyage.
        /// </summary>
        public int CompletedMissions { get; set; } = 0;

        /// <summary>
        /// Failed missions in this voyage.
        /// </summary>
        public int FailedMissions { get; set; } = 0;

        /// <summary>
        /// In-progress missions in this voyage.
        /// </summary>
        public int InProgressMissions { get; set; } = 0;

        /// <summary>
        /// Mission counts keyed by mission status name (only statuses with at least one mission appear).
        /// </summary>
        [CollectionBound(MaxStatusEntries, Reason = "At most one entry per MissionStatusEnum value")]
        public Dictionary<string, int> MissionCountsByStatus { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Vessel of the voyage's earliest mission (the vessel a voyage is dispatched to), or null when the voyage has no
        /// missions.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Number of distinct vessels the voyage's missions target. Usually 1; more when missions on other vessels were
        /// added to the voyage later.
        /// </summary>
        public int VesselCount { get; set; } = 0;

        /// <summary>
        /// Upper bound on <see cref="MissionCountsByStatus"/> entries: the number of <see cref="MissionStatusEnum"/>
        /// values is below it (a test asserts this).
        /// </summary>
        public const int MaxStatusEntries = 32;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VoyageProgress()
        {
        }

        #endregion
    }
}
