namespace Armada.Core.Settings
{
    /// <summary>
    /// Routing from outcome data (RS-8.5, RS-8.6): ranking captains within a tier by their recorded results for the
    /// persona, and suggesting model profile tiers. Both are off by default; with them off, routing is the 1.4.0 order.
    /// All values apply live.
    /// </summary>
    public class RoutingDataSettings
    {
        #region Public-Members

        /// <summary>
        /// Smallest minimum sample size (5).
        /// </summary>
        public const int MinimumSamplesFloor = 5;

        /// <summary>
        /// Largest minimum sample size (1000).
        /// </summary>
        public const int MinimumSamplesCeiling = 1000;

        /// <summary>
        /// Shortest half-life in days (1).
        /// </summary>
        public const int HalfLifeDaysFloor = 1;

        /// <summary>
        /// Longest half-life in days (365).
        /// </summary>
        public const int HalfLifeDaysCeiling = 365;

        /// <summary>
        /// Rank the captains of one tier by their model's recorded results for the mission's persona (success, Judge pass
        /// rate, no-op rate, tool-call validity, repair rate), most successful first. A captain whose model has fewer than
        /// <see cref="MinimumSamples"/> attempts keeps the previous order (load, then last update) after the ranked
        /// captains. Persona captain priorities and the Judge's preference for captains that did not author the reviewed
        /// work still come first. Default false.
        /// </summary>
        public bool RankByOutcomes { get; set; } = false;

        /// <summary>
        /// Suggest a model profile tier from its outcome data during the model alert pass and raise a TierSuggestion
        /// alert in Needs You. A suggestion never changes routing until an administrator applies it. Default false.
        /// </summary>
        public bool SuggestTiers { get; set; } = false;

        /// <summary>
        /// Attempts a model needs for a persona before its results rank it or suggest a tier. Default 20, clamped to
        /// 5..1000.
        /// </summary>
        public int MinimumSamples
        {
            get => _MinimumSamples;
            set => _MinimumSamples = value < MinimumSamplesFloor ? MinimumSamplesFloor : (value > MinimumSamplesCeiling ? MinimumSamplesCeiling : value);
        }

        /// <summary>
        /// Days after which an attempt counts half as much as a new one. Default 14, clamped to 1..365.
        /// </summary>
        public int HalfLifeDays
        {
            get => _HalfLifeDays;
            set => _HalfLifeDays = value < HalfLifeDaysFloor ? HalfLifeDaysFloor : (value > HalfLifeDaysCeiling ? HalfLifeDaysCeiling : value);
        }

        #endregion

        #region Private-Members

        private int _MinimumSamples = 20;
        private int _HalfLifeDays = 14;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RoutingDataSettings()
        {
        }

        #endregion
    }
}
