namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Query parameters for paginated enumeration of entities.
    /// Supports page-based pagination, date filtering, ordering, and entity-specific filters.
    /// </summary>
    public class EnumerationQuery
    {
        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 100;

        #endregion

        #region Public-Members

        /// <summary>
        /// Page number (1-based). Default is 1.
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Number of results per page. Default is 100. Min 1, max 1000.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set
            {
                if (value < 1) _PageSize = 1;
                else if (value > 1000) _PageSize = 1000;
                else _PageSize = value;
            }
        }

        /// <summary>
        /// Sort order. Default is CreatedDescending (newest first).
        /// </summary>
        public EnumerationOrderEnum Order { get; set; } = EnumerationOrderEnum.CreatedDescending;

        /// <summary>
        /// Filter to entities created after this timestamp (exclusive).
        /// </summary>
        public DateTime? CreatedAfter { get; set; }

        /// <summary>
        /// Filter to entities created before this timestamp (exclusive).
        /// </summary>
        public DateTime? CreatedBefore { get; set; }

        /// <summary>
        /// Filter by status (entity-specific: MissionStatusEnum, VoyageStatusEnum, CaptainStateEnum, MergeStatusEnum).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Filter by fleet identifier.
        /// </summary>
        public string? FleetId { get; set; }

        /// <summary>
        /// Filter by vessel identifier.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Filter by captain identifier.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Filter by voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Filter by mission identifier.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Filter by event type (events only).
        /// </summary>
        public string? EventType { get; set; }

        /// <summary>
        /// Filter by signal type (signals only).
        /// </summary>
        public string? SignalType { get; set; }

        /// <summary>
        /// Filter by recipient captain identifier (signals only).
        /// </summary>
        public string? ToCaptainId { get; set; }

        /// <summary>
        /// Whether to return only unread signals (signals only).
        /// </summary>
        public bool? UnreadOnly { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public EnumerationQuery()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Calculate the SQL OFFSET for this query.
        /// </summary>
        [JsonIgnore]
        public int Offset => (PageNumber - 1) * PageSize;

        /// <summary>
        /// Apply querystring overrides. Any non-null/non-empty querystring value replaces the body value.
        /// </summary>
        /// <param name="queryGetter">Function that retrieves a querystring value by key.</param>
        public void ApplyQuerystringOverrides(Func<string, string?> queryGetter)
        {
            string? val;

            val = queryGetter("pageNumber");
            if (!String.IsNullOrEmpty(val) && int.TryParse(val, out int pn)) PageNumber = pn;

            val = queryGetter("pageSize");
            if (!String.IsNullOrEmpty(val) && int.TryParse(val, out int ps)) PageSize = ps;

            val = queryGetter("order");
            if (!String.IsNullOrEmpty(val) && Enum.TryParse<EnumerationOrderEnum>(val, true, out EnumerationOrderEnum ord)) Order = ord;

            val = queryGetter("createdAfter");
            if (!String.IsNullOrEmpty(val) && DateTime.TryParse(val, out DateTime ca)) CreatedAfter = ca;

            val = queryGetter("createdBefore");
            if (!String.IsNullOrEmpty(val) && DateTime.TryParse(val, out DateTime cb)) CreatedBefore = cb;

            val = queryGetter("status");
            if (!String.IsNullOrEmpty(val)) Status = val;

            val = queryGetter("fleetId");
            if (!String.IsNullOrEmpty(val)) FleetId = val;

            val = queryGetter("vesselId");
            if (!String.IsNullOrEmpty(val)) VesselId = val;

            val = queryGetter("captainId");
            if (!String.IsNullOrEmpty(val)) CaptainId = val;

            val = queryGetter("voyageId");
            if (!String.IsNullOrEmpty(val)) VoyageId = val;

            val = queryGetter("missionId");
            if (!String.IsNullOrEmpty(val)) MissionId = val;

            val = queryGetter("type");
            if (!String.IsNullOrEmpty(val)) EventType = val;

            val = queryGetter("signalType");
            if (!String.IsNullOrEmpty(val)) SignalType = val;

            val = queryGetter("toCaptainId");
            if (!String.IsNullOrEmpty(val)) ToCaptainId = val;

            val = queryGetter("unreadOnly");
            if (!String.IsNullOrEmpty(val) && bool.TryParse(val, out bool uo)) UnreadOnly = uo;
        }

        #endregion
    }
}
