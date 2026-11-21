using System;

namespace Armada.Core.Models
{
    /// <summary>
    /// Query and scope parameters for request-history storage and APIs.
    /// </summary>
    public class RequestHistoryQuery
    {
        #region Public-Members

        /// <summary>
        /// Tenant scope or filter.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User scope or filter.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Credential filter.
        /// </summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>
        /// Principal-display filter.
        /// </summary>
        public string? Principal { get; set; } = null;

        /// <summary>
        /// HTTP method filter.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// True to leave out read requests (GET, HEAD and OPTIONS), as the history timeline does unless asked for them.
        /// Applied in the query, so a large request history is not read only to be dropped.
        /// </summary>
        public bool ExcludeReadMethods { get; set; } = false;

        /// <summary>
        /// Route filter.
        /// </summary>
        public string? Route { get; set; } = null;

        /// <summary>
        /// Exact status-code filter.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Optional success-state filter.
        /// </summary>
        public bool? IsSuccess { get; set; } = null;

        /// <summary>
        /// Lower bound on creation timestamp.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Upper bound on creation timestamp.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// One-based page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Summary bucket width in minutes.
        /// </summary>
        public int BucketMinutes { get; set; } = 15;

        /// <summary>
        /// Calculated zero-based offset.
        /// </summary>
        public int Offset => PageNumber <= 1 ? 0 : (PageNumber - 1) * PageSize;

        #endregion
    }
}
