namespace Armada.Core.Models
{
    using System;

    /// <summary>Bounded history filters applied in SQL after tenant/mission/owner authorization.</summary>
    public sealed class CaptainSelectionHistoryQuery : EnumerationQuery
    {
        /// <summary>Exact semantic trigger, or null for every trigger.</summary>
        public string? Trigger { get; set; }
        /// <summary>Admitted or Waiting, or null for either.</summary>
        public string? Outcome { get; set; }
        /// <summary>Inclusive UTC evaluation start.</summary>
        public DateTime? FromUtc { get; set; }
        /// <summary>Exclusive UTC evaluation end.</summary>
        public DateTime? ToUtc { get; set; }
        /// <summary>Create a first-page query with 25 rows.</summary>
        public CaptainSelectionHistoryQuery() { PageSize = 25; }
    }
}
