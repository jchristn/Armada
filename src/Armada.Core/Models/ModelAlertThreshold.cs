namespace Armada.Core.Models
{
    /// <summary>
    /// Alert thresholds of one persona (an override) or the built-in defaults (docs/MODEL_HETEROGENEITY.md, Alerts,
    /// Decision 13). In an override a null value keeps the default.
    /// </summary>
    public class ModelAlertThreshold
    {
        #region Public-Members

        /// <summary>Default first-pass success floor: below 50% raises an alert.</summary>
        public const double DefaultFirstPassMinRate = 0.5;

        /// <summary>Default minimum missions before first-pass success is judged.</summary>
        public const int DefaultFirstPassMinMissions = 8;

        /// <summary>Default escalations away in a row that raise an alert.</summary>
        public const int DefaultEscalationsInARow = 3;

        /// <summary>Default protocol repair ceiling: above 30% raises an alert.</summary>
        public const double DefaultProtocolRepairMaxRate = 0.3;

        /// <summary>Default minimum attempts before the protocol repair rate is judged.</summary>
        public const int DefaultProtocolRepairMinAttempts = 8;

        /// <summary>Default multiple of the persona median that raises a cost alert.</summary>
        public const double DefaultCostMedianMultiplier = 2.0;

        /// <summary>Default minimum accepted results on each side of the cost comparison.</summary>
        public const int DefaultCostMinAccepted = 5;

        /// <summary>
        /// Persona the override applies to; null for the defaults.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// First-pass success below this rate (0 to 1) raises an alert.
        /// </summary>
        public double? FirstPassMinRate { get; set; } = null;

        /// <summary>
        /// Missions (1 to 1000) needed before first-pass success is judged.
        /// </summary>
        public int? FirstPassMinMissions { get; set; } = null;

        /// <summary>
        /// Escalations away from the profile in a row (1 to 1000) that raise an alert.
        /// </summary>
        public int? EscalationsInARow { get; set; } = null;

        /// <summary>
        /// Protocol repair rate above this (0 to 1) raises an alert.
        /// </summary>
        public double? ProtocolRepairMaxRate { get; set; } = null;

        /// <summary>
        /// Attempts (1 to 1000) needed before the protocol repair rate is judged.
        /// </summary>
        public int? ProtocolRepairMinAttempts { get; set; } = null;

        /// <summary>
        /// Tokens per accepted result above this multiple (1 to 100) of the persona median across other profiles raises an
        /// alert.
        /// </summary>
        public double? CostMedianMultiplier { get; set; } = null;

        /// <summary>
        /// Accepted results (1 to 1000) the profile and each compared profile need for the cost rule.
        /// </summary>
        public int? CostMinAccepted { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with every value null.
        /// </summary>
        public ModelAlertThreshold()
        {
        }

        /// <summary>
        /// The built-in defaults (Decision 13).
        /// </summary>
        /// <returns>A new threshold with every value set.</returns>
        public static ModelAlertThreshold BuiltInDefaults()
        {
            return new ModelAlertThreshold
            {
                FirstPassMinRate = DefaultFirstPassMinRate,
                FirstPassMinMissions = DefaultFirstPassMinMissions,
                EscalationsInARow = DefaultEscalationsInARow,
                ProtocolRepairMaxRate = DefaultProtocolRepairMaxRate,
                ProtocolRepairMinAttempts = DefaultProtocolRepairMinAttempts,
                CostMedianMultiplier = DefaultCostMedianMultiplier,
                CostMinAccepted = DefaultCostMinAccepted
            };
        }

        #endregion
    }
}
