namespace Armada.Core.Models
{
    /// <summary>
    /// What a model scorecard covers: one persona of one model profile, optionally one vessel, over a rolling window of
    /// the last <see cref="WindowDays"/> days or the last <see cref="WindowMissions"/> missions, whichever is smaller.
    /// </summary>
    public class ModelScorecardRequest
    {
        #region Public-Members

        /// <summary>
        /// Default window length in days.
        /// </summary>
        public const int DefaultWindowDays = 30;

        /// <summary>
        /// Largest window length in days.
        /// </summary>
        public const int MaxWindowDays = 365;

        /// <summary>
        /// Default window length in missions.
        /// </summary>
        public const int DefaultWindowMissions = 50;

        /// <summary>
        /// Largest window length in missions.
        /// </summary>
        public const int MaxWindowMissions = 500;

        /// <summary>
        /// Persona the scorecard is for (required), for example Worker or Judge. Compared case-insensitively.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Only stages on this vessel, or null for every vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Window length in days (1 to 365), or null for 30.
        /// </summary>
        public int? WindowDays { get; set; } = null;

        /// <summary>
        /// Window length in missions (1 to 500), or null for 50.
        /// </summary>
        public int? WindowMissions { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelScorecardRequest()
        {
        }

        #endregion
    }
}
