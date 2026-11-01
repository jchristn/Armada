namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Result of one alert evaluation pass: what was evaluated, the result of every rule (inactive rules say why), and
    /// the alerts raised, kept open and resolved. The pass only reads scorecard data and writes alert rows.
    /// </summary>
    public class ModelAlertEvaluation
    {
        /// <summary>
        /// Most targets listed in <see cref="Targets"/> (1000, the page size limit).
        /// </summary>
        public const int MaxTargets = CollectionLimits.MaxPageSize;

        /// <summary>
        /// Most alerts listed in <see cref="Raised"/> (1000, the page size limit).
        /// </summary>
        public const int MaxRaised = CollectionLimits.MaxPageSize;

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
        /// Every evaluated target and its rule results, in evaluation order (at most <see cref="MaxTargets"/>).
        /// </summary>
        [CollectionBound(MaxTargets, Reason = "Targets past the limit are dropped and Truncated is set")]
        public List<ModelAlertTarget> Targets { get; set; } = new List<ModelAlertTarget>();

        /// <summary>
        /// Alerts raised for the first time or reopened (at most <see cref="MaxRaised"/>).
        /// </summary>
        [CollectionBound(MaxRaised, Reason = "Raised alerts past the limit are dropped from the result (they stay in the inbox) and Truncated is set")]
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
        /// True when the stage outcome read hit its cap, so some pairs may be missing, or when <see cref="Targets"/> or
        /// <see cref="Raised"/> was cut to its limit.
        /// </summary>
        public bool Truncated { get; set; } = false;
    }
}
