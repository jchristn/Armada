namespace Armada.Core.Harbor
{
    using System;

    /// <summary>
    /// What a launch is about, in words a person reads, so a Harbor can describe the job in its Running now list: the
    /// mission and its vessel and voyage, the captain, the pipeline stage, the branch, or the Ask conversation and the
    /// question. Every field is optional and informational; the Harbor never acts on them.
    /// </summary>
    public class HarborLaunchDisplay
    {
        #region Public-Members

        /// <summary>
        /// Longest <see cref="AskQuestion"/> the Admiral sends, in characters.
        /// </summary>
        public const int MaxQuestionChars = 200;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string? MissionTitle { get; set; } = null;

        /// <summary>
        /// Name of the mission's vessel.
        /// </summary>
        public string? VesselName { get; set; } = null;

        /// <summary>
        /// The mission's voyage.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Title of the mission's voyage.
        /// </summary>
        public string? VoyageTitle { get; set; } = null;

        /// <summary>
        /// The mission's position in its voyage, from 1, or null.
        /// </summary>
        public int? VoyagePosition { get; set; } = null;

        /// <summary>
        /// How many missions the voyage has, or null.
        /// </summary>
        public int? VoyageMissionCount { get; set; } = null;

        /// <summary>
        /// Captain name.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// The model the captain runs, when it names one.
        /// </summary>
        public string? CaptainModel { get; set; } = null;

        /// <summary>
        /// Pipeline stage or persona the mission runs as (for example "Implement" or "Judge").
        /// </summary>
        public string? Stage { get; set; } = null;

        /// <summary>
        /// The mission's branch.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// The Ask Armada conversation an Ask turn belongs to.
        /// </summary>
        public string? AskThreadId { get; set; } = null;

        /// <summary>
        /// The user's question of an Ask turn: its first line, at most <see cref="MaxQuestionChars"/> characters.
        /// </summary>
        public string? AskQuestion { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy.
        /// </summary>
        /// <returns>The copy.</returns>
        public HarborLaunchDisplay Clone()
        {
            return new HarborLaunchDisplay
            {
                MissionTitle = MissionTitle,
                VesselName = VesselName,
                VoyageId = VoyageId,
                VoyageTitle = VoyageTitle,
                VoyagePosition = VoyagePosition,
                VoyageMissionCount = VoyageMissionCount,
                CaptainName = CaptainName,
                CaptainModel = CaptainModel,
                Stage = Stage,
                BranchName = BranchName,
                AskThreadId = AskThreadId,
                AskQuestion = AskQuestion
            };
        }

        /// <summary>
        /// Whether no field is set.
        /// </summary>
        /// <returns>True when empty.</returns>
        public bool IsEmpty()
        {
            return String.IsNullOrWhiteSpace(MissionTitle)
                && String.IsNullOrWhiteSpace(VesselName)
                && String.IsNullOrWhiteSpace(VoyageId)
                && String.IsNullOrWhiteSpace(VoyageTitle)
                && !VoyagePosition.HasValue
                && !VoyageMissionCount.HasValue
                && String.IsNullOrWhiteSpace(CaptainName)
                && String.IsNullOrWhiteSpace(CaptainModel)
                && String.IsNullOrWhiteSpace(Stage)
                && String.IsNullOrWhiteSpace(BranchName)
                && String.IsNullOrWhiteSpace(AskThreadId)
                && String.IsNullOrWhiteSpace(AskQuestion);
        }

        #endregion
    }
}
