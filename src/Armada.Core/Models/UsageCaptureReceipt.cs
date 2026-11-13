namespace Armada.Core.Models
{
    using System;

    /// <summary>Receipt for an accepted single-flight usage capture.</summary>
    public class UsageCaptureReceipt
    {
        /// <summary>JobId: account usage contract.</summary>
        public string? JobId { get; set; }
        /// <summary>Status: account usage contract.</summary>
        public string Status { get; set; } = "Queued";
        /// <summary>JoinedExisting: account usage contract.</summary>
        public bool JoinedExisting { get; set; }
        /// <summary>NextEligibleUtc: account usage contract.</summary>
        public DateTime? NextEligibleUtc { get; set; }
    }
}
