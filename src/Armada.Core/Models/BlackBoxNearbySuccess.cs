namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A successful landing on the same vessel near a failure that touched some of the same files (Related section and
    /// GET /api/v1/blackbox/{id}/nearby-successes).
    /// </summary>
    public class BlackBoxNearbySuccess
    {
        #region Public-Members

        /// <summary>
        /// Success record (bbs_ prefix).
        /// </summary>
        public string SuccessId { get; set; } = "";

        /// <summary>
        /// Mission that landed.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Its title.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// When it landed.
        /// </summary>
        public DateTime LandedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hours between the landing and the failure (negative when it landed before the failure).
        /// </summary>
        public double HoursFromFailure { get; set; } = 0;

        /// <summary>
        /// Its start commit, or null.
        /// </summary>
        public string? BaseCommit { get; set; } = null;

        /// <summary>
        /// Its landed commit, or null.
        /// </summary>
        public string? TargetCommit { get; set; } = null;

        /// <summary>
        /// Its run time in milliseconds, or null.
        /// </summary>
        public long? RunMs { get; set; } = null;

        /// <summary>
        /// Files it changed that the failure also touched, first 20.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxWarningFiles, Reason = "First 20; OverlappingFileCount has the total")]
        public List<string> OverlappingFiles
        {
            get => _OverlappingFiles;
            set => _OverlappingFiles = value ?? new List<string>();
        }

        /// <summary>
        /// How many files overlap.
        /// </summary>
        public int OverlappingFileCount { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<string> _OverlappingFiles = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxNearbySuccess()
        {
        }

        #endregion
    }
}
