namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Query parameters for skill APIs and storage.
    /// </summary>
    public class SkillQuery
    {
        #region Private-Members

        private int _PageSize = 10;

        #endregion

        /// <summary>
        /// Tenant scope or filter.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User scope or filter.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// When true, only tenant-wide skills plus the user-specific skills owned by <see cref="ViewerUserId"/> are
        /// returned (the visibility rule for a caller who is neither a global nor a tenant admin), applied in the data source
        /// before counting. Server-set; never read from a request body.
        /// </summary>
        [JsonIgnore]
        public bool RestrictToViewer { get; set; } = false;

        /// <summary>
        /// Viewer whose user-specific skills are included when <see cref="RestrictToViewer"/> is true.
        /// </summary>
        [JsonIgnore]
        public string? ViewerUserId { get; set; } = null;

        /// <summary>
        /// Category filter.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// Optional name/description search text.
        /// </summary>
        public string? Search { get; set; } = null;

        /// <summary>
        /// Active-state filter.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Results per page (1 to 1000, default 10). Values outside the range are clamped, so a request can never ask for
        /// an unbounded page.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = value < 1 ? 1 : (value > 1000 ? 1000 : value);
        }

        /// <summary>
        /// Created-after filter.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Created-before filter.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Zero-based offset.
        /// </summary>
        public int Offset => PageNumber <= 1 ? 0 : (PageNumber - 1) * PageSize;
    }
}
