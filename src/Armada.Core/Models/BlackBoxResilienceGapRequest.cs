namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Filters and paging for Armada resilience gaps (entries triaged ArmadaDefect in handoff and landing scope, grouped by
    /// fingerprint). Ordered by severity, then occurrences, then last seen. Page size at most 50.
    /// </summary>
    public class BlackBoxResilienceGapRequest : EnumerationQuery
    {
        /// <summary>
        /// Count only entries with this resolution (null for every resolution).
        /// </summary>
        public BlackBoxResolutionEnum? Resolution { get; set; } = null;

        /// <summary>
        /// Only this phase, or null for the whole scope.
        /// </summary>
        public BlackBoxPhaseEnum? Phase { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxResilienceGapRequest()
        {
        }
    }
}
