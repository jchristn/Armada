namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Query of the recovery statistics (GET /api/v1/blackbox/recovery-stats and its enumerate twin): paging plus an
    /// optional rule filter (VesselId filters by vessel).
    /// </summary>
    public class BlackBoxRecoveryStatsRequest : EnumerationQuery
    {
        #region Public-Members

        /// <summary>
        /// Rule filter (for example BBX-LAND-001), or null.
        /// </summary>
        public string? RuleId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryStatsRequest()
        {
        }

        #endregion
    }
}
