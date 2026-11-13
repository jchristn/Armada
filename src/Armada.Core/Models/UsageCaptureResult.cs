namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;
    /// <summary>Capture union: queued receipt, or the original paged passive observations.</summary>
    public class UsageCaptureResult : UsageCaptureReceipt
    {
        /// <summary>Initialize the passive variant; active receipts deserialize Status as Queued.</summary>
        public UsageCaptureResult() { Status = "Passive"; }

        /// <summary>Passive observations; null for queued active captures.</summary>
        [CollectionBound(100)]
        public List<UsageSnapshotView>? Objects { get; set; }
        /// <summary>Passive page number.</summary>
        public int PageNumber { get; set; }
        /// <summary>Passive page size.</summary>
        public int PageSize { get; set; }
        /// <summary>Passive matching group count.</summary>
        public long TotalRecords { get; set; }
        /// <summary>Passive page count.</summary>
        public int TotalPages { get; set; }
    }
}
