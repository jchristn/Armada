namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The routing consequence of one usage observation for one captain, produced by
    /// <see cref="UsageQuotaPolicy.Evaluate"/> and applied by <see cref="CaptainService.ApplyUsageQuotaAsync"/>.
    /// </summary>
    public class UsageQuotaDecision
    {
        #region Public-Members

        /// <summary>
        /// What the observation asks for.
        /// </summary>
        public UsageQuotaActionEnum Action { get; set; } = UsageQuotaActionEnum.NoChange;

        /// <summary>
        /// Stable reason code (for example NotAuthoritative, Stale, WrongAccount, Exhausted, BelowLimit).
        /// </summary>
        public string ReasonCode { get; set; } = "NoChange";

        /// <summary>
        /// Capture time of the observation; the compare-and-set key against an existing quota-owned hold.
        /// </summary>
        public DateTime ObservedUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// For a Hold, when the hold ends (the latest reset among the exhausted windows, or the next
        /// freshness horizon when no reset was reported). Null otherwise.
        /// </summary>
        public DateTime? HoldUntilUtc { get; set; } = null;

        /// <summary>
        /// Names of the exhausted applicable windows behind a Hold. Never null.
        /// </summary>
        public List<string> ExhaustedWindows { get; set; } = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageQuotaDecision()
        {
        }

        /// <summary>
        /// A decision that changes nothing.
        /// </summary>
        /// <param name="reasonCode">Stable reason code.</param>
        /// <param name="observedUtc">Observation time.</param>
        /// <returns>The decision.</returns>
        public static UsageQuotaDecision NoChange(string reasonCode, DateTime observedUtc)
        {
            return new UsageQuotaDecision { Action = UsageQuotaActionEnum.NoChange, ReasonCode = reasonCode, ObservedUtc = observedUtc };
        }

        #endregion
    }
}
