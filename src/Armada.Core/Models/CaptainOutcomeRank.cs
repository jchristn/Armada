namespace Armada.Core.Models
{
    using System;
    using System.Globalization;

    /// <summary>
    /// One captain's place in an outcome-ranked routing bucket (RS-8.6), recorded on the mission.routing_ranked event so
    /// the selection says why a captain was chosen.
    /// </summary>
    public class CaptainOutcomeRank
    {
        #region Public-Members

        /// <summary>
        /// Event type of the ranking record.
        /// </summary>
        public const string EventType = "mission.routing_ranked";

        /// <summary>
        /// Most captains recorded per decision (10).
        /// </summary>
        public const int MaxListed = 10;

        /// <summary>
        /// Captain identifier.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Captain name.
        /// </summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>
        /// Attempts the score is based on, or null when the captain's model had too few to rank.
        /// </summary>
        public int? Samples { get; set; } = null;

        /// <summary>
        /// Outcome score, 0 to 1, or null when unranked.
        /// </summary>
        public double? Score { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainOutcomeRank()
        {
        }

        /// <summary>
        /// The rank entry of a captain.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <param name="score">Its score, or null when unranked.</param>
        /// <returns>The entry.</returns>
        /// <exception cref="ArgumentNullException">The captain is null.</exception>
        public static CaptainOutcomeRank From(Captain captain, PersonaOutcomeScore? score)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            return new CaptainOutcomeRank { CaptainId = captain.Id, CaptainName = captain.Name, Samples = score?.Samples, Score = score?.Score };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A short description, for example "qwen 82% (n=41)" or "new-model unranked".
        /// </summary>
        /// <returns>Text.</returns>
        public string Describe()
        {
            string name = String.IsNullOrEmpty(CaptainName) ? CaptainId : CaptainName!;
            if (!Score.HasValue) return name + " unranked";
            return name + " " + (Score.Value * 100).ToString("0", CultureInfo.InvariantCulture) + "% (n=" + Samples + ")";
        }

        #endregion
    }
}
