namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Capture entries for historical failed missions and voyages that have none (Admin).
    /// </summary>
    public class BlackBoxBackfillRequest
    {
        /// <summary>
        /// Only subjects updated at or after this time (UTC); null for all.
        /// </summary>
        public DateTime? Since { get; set; } = null;

        /// <summary>
        /// Most subjects captured by one run (1 to 100000).
        /// </summary>
        public int MaxSubjects { get; set; } = 1000;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBackfillRequest()
        {
        }
    }
}
