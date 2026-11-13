namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;
    using Armada.Core.Services;

    /// <summary>
    /// The Ask Armada black box card (MISSION_BLACKBOX_PLAN.md section 10.3): an entry's summary with its status chips
    /// (triage, kind, step, capture mode, resolution), the missions that conflicted with it, its top recovery paths and
    /// configuration recommendations, and the follow-ups offered after an answer. Built from the entry as the thread's
    /// owner sees it; every value comes from the entry.
    /// </summary>
    public class AskBlackBoxCard
    {
        #region Public-Members

        /// <summary>
        /// The entry's summary (where, how, status before and after, why, top recovery paths).
        /// </summary>
        public BlackBoxDigest Digest
        {
            get => _Digest;
            set => _Digest = value ?? new BlackBoxDigest();
        }

        /// <summary>
        /// The entry's fingerprint (similar failures share it), or null.
        /// </summary>
        public string? Fingerprint { get; set; } = null;

        /// <summary>
        /// Missions that landed changes the failed mission conflicted with (most relevant first).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxConflictingMissions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxConflictingMission> ConflictingMissions
        {
            get => _ConflictingMissions;
            set => _ConflictingMissions = value ?? new List<BlackBoxConflictingMission>();
        }

        /// <summary>
        /// The first conflicted files (ConflictedFileTotal on the digest holds the real count).
        /// </summary>
        [CollectionBound(MaxConflictedFiles, Reason = "The card shows the first ten; the full list is in the entry's Conflict section")]
        public List<string> ConflictedFiles
        {
            get => _ConflictedFiles;
            set => _ConflictedFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Top configuration recommendations.
        /// </summary>
        [CollectionBound(MaxRecommendations, Reason = "The card shows the top three; the rest are in the entry's Analysis section")]
        public List<AskBlackBoxRecommendation> Recommendations
        {
            get => _Recommendations;
            set => _Recommendations = value ?? new List<AskBlackBoxRecommendation>();
        }

        /// <summary>
        /// Follow-ups offered after the answer: Fix it, Apply the configuration change, Draft an Armada fix mission and
        /// Compare with similar failures (those that apply to this entry).
        /// </summary>
        [CollectionBound(MaxFollowUps, Reason = "One per follow-up kind")]
        public List<AskBlackBoxFollowUp> FollowUps
        {
            get => _FollowUps;
            set => _FollowUps = value ?? new List<AskBlackBoxFollowUp>();
        }

        /// <summary>
        /// Notes on the entry.
        /// </summary>
        public long NoteCount { get; set; } = 0;

        /// <summary>
        /// Conflicted files shown on the card.
        /// </summary>
        public const int MaxConflictedFiles = 10;

        /// <summary>
        /// Recommendations shown on the card.
        /// </summary>
        public const int MaxRecommendations = 3;

        /// <summary>
        /// Follow-ups on the card.
        /// </summary>
        public const int MaxFollowUps = 4;

        #endregion

        #region Private-Members

        private BlackBoxDigest _Digest = new BlackBoxDigest();
        private List<BlackBoxConflictingMission> _ConflictingMissions = new List<BlackBoxConflictingMission>();
        private List<string> _ConflictedFiles = new List<string>();
        private List<AskBlackBoxRecommendation> _Recommendations = new List<AskBlackBoxRecommendation>();
        private List<AskBlackBoxFollowUp> _FollowUps = new List<AskBlackBoxFollowUp>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskBlackBoxCard()
        {
        }

        #endregion
    }
}
