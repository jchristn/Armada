namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Mission-source consumption grouped by persisted retry attribution and currency.</summary>
    public class UsageMissionRow
    {
        /// <summary>MissionId captured or safely joined for this row.</summary>
        public string MissionId { get; set; } = string.Empty;

        /// <summary>Title captured or safely joined for this row.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Status captured or safely joined for this row.</summary>
        public string? Status { get; set; } = null;

        /// <summary>VesselId captured or safely joined for this row.</summary>
        public string? VesselId { get; set; } = null;

        /// <summary>VesselName captured or safely joined for this row.</summary>
        public string? VesselName { get; set; } = null;

        /// <summary>CaptainId captured or safely joined for this row.</summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>CaptainName captured or safely joined for this row.</summary>
        public string? CaptainName { get; set; } = null;

        /// <summary>MembershipId captured or safely joined for this row.</summary>
        public string? MembershipId { get; set; } = null;

        /// <summary>AccountKey captured or safely joined for this row.</summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>HostId captured or safely joined for this row.</summary>
        public string? HostId { get; set; } = null;

        /// <summary>MissionUrl captured or safely joined for this row.</summary>
        public string? MissionUrl { get; set; } = null;

        /// <summary>CompletedUtc captured or safely joined for this row.</summary>
        public DateTime CompletedUtc { get; set; }

        /// <summary>InputTokens captured or safely joined for this row.</summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>OutputTokens captured or safely joined for this row.</summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>CachedTokens captured or safely joined for this row.</summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>TotalTokens captured or safely joined for this row.</summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>ReportedCost captured or safely joined for this row.</summary>
        public double? ReportedCost { get; set; } = null;

        /// <summary>CostCurrency captured or safely joined for this row.</summary>
        public string? CostCurrency { get; set; } = null;

        /// <summary>Estimated captured or safely joined for this row.</summary>
        public bool Estimated { get; set; } = false;

        /// <summary>CostCoverage captured or safely joined for this row.</summary>
        public double CostCoverage { get; set; } = 0;

    }
}
