namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One kind of automatic recovery Armada attempted before the failure, with its outcome.
    /// </summary>
    public class BlackBoxAutoRecovery
    {
        /// <summary>
        /// Kind of recovery.
        /// </summary>
        public BlackBoxAutoRecoveryKindEnum Kind { get; set; } = BlackBoxAutoRecoveryKindEnum.Unknown;

        /// <summary>
        /// Attempts recorded.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Last attempt (UTC), or null when only a counter recorded it.
        /// </summary>
        public DateTime? LastUtc { get; set; } = null;

        /// <summary>
        /// Outcome: Succeeded (it worked, the failure came later), Failed (it ran and did not help), Expired (retries or
        /// the deferral ran out) or Unknown.
        /// </summary>
        public BlackBoxRecoveryOutcomeEnum Outcome { get; set; } = BlackBoxRecoveryOutcomeEnum.Unknown;

        /// <summary>
        /// Detail (the last attempt's message), or null.
        /// </summary>
        public string? Detail { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAutoRecovery()
        {
        }
    }
}
