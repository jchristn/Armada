namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of one alert evaluation pass: what was evaluated, the result of every rule (inactive rules say why), and
    /// the alerts raised, kept open and resolved. The pass only reads scorecard data and writes alert rows.
    /// </summary>
    public class ModelAlertEvaluation
    {
        /// <summary>
        /// When the pass ran.
        /// </summary>
        public DateTime EvaluatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Tenant evaluated, or null for every tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// (profile, persona) pairs with recent production attempts.
        /// </summary>
        public int Pairs { get; set; } = 0;

        /// <summary>
        /// Every evaluated target and its rule results.
        /// </summary>
        public List<ModelAlertTarget> Targets { get; set; } = new List<ModelAlertTarget>();

        /// <summary>
        /// Alerts raised for the first time or reopened.
        /// </summary>
        public List<ModelAlert> Raised { get; set; } = new List<ModelAlert>();

        /// <summary>
        /// Alerts that were open and are still open.
        /// </summary>
        public int StillOpen { get; set; } = 0;

        /// <summary>
        /// Alerts resolved because the condition no longer holds.
        /// </summary>
        public int Resolved { get; set; } = 0;

        /// <summary>
        /// True when the stage outcome read hit its cap, so some pairs may be missing.
        /// </summary>
        public bool Truncated { get; set; } = false;
    }
}
