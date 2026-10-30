namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Filters and paging for failure-mode fingerprints. Ordered by count descending, then last seen descending, then fingerprint.
    /// </summary>
    public class BlackBoxFingerprintEnumerateRequest : EnumerationQuery
    {
        /// <summary>
        /// Count only entries with this resolution.
        /// </summary>
        public BlackBoxResolutionEnum? Resolution { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFingerprintEnumerateRequest()
        {
        }
    }
}
