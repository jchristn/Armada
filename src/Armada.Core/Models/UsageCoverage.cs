namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Coverage of observed token data; quota deltas never imply mission consumption.</summary>
    public class UsageCoverage
    {
        /// <summary>Records without captured account membership.</summary>
        public long UnattributedRecords { get; set; } = 0;

        /// <summary>Unattributed records excluded by account filtering.</summary>
        public long ExcludedUnattributedRecords { get; set; } = 0;

        /// <summary>Records from chat, planning or other work.</summary>
        public long NonMissionRecords { get; set; } = 0;

        /// <summary>Distinct mission-source token records.</summary>
        public long MissionRecords { get; set; } = 0;

        /// <summary>Records with a reported cost and currency.</summary>
        public long KnownCostRecords { get; set; } = 0;

        /// <summary>More than twenty series match; narrow identity filters.</summary>
        public bool SeriesTruncated { get; set; } = false;

        /// <summary>Coverage explanation.</summary>
        public string? Warning { get; set; } = null;

    }
}
