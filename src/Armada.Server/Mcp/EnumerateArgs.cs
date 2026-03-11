namespace Armada.Server.Mcp
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MCP tool arguments for the unified enumeration tool.
    /// </summary>
    public class EnumerateArgs
    {
        /// <summary>
        /// Entity type to enumerate: fleets, vessels, captains, missions, voyages, docks, signals, events.
        /// </summary>
        public string EntityType { get; set; } = "";

        /// <summary>
        /// Page number (1-based, default 1).
        /// </summary>
        public int? PageNumber { get; set; }

        /// <summary>
        /// Results per page (default 100, max 1000).
        /// </summary>
        public int? PageSize { get; set; }

        /// <summary>
        /// Sort order: CreatedAscending, CreatedDescending.
        /// </summary>
        public string? Order { get; set; }

        /// <summary>
        /// ISO 8601 timestamp — only return entities created after this time.
        /// </summary>
        public string? CreatedAfter { get; set; }

        /// <summary>
        /// ISO 8601 timestamp — only return entities created before this time.
        /// </summary>
        public string? CreatedBefore { get; set; }

        /// <summary>
        /// Filter by status.
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Filter by fleet ID.
        /// </summary>
        public string? FleetId { get; set; }

        /// <summary>
        /// Filter by vessel ID.
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Filter by captain ID.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Filter by voyage ID.
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Filter by mission ID.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Filter by event type string.
        /// </summary>
        public string? EventType { get; set; }

        /// <summary>
        /// Filter by signal type.
        /// </summary>
        public string? SignalType { get; set; }

        /// <summary>
        /// Filter by recipient captain ID.
        /// </summary>
        public string? ToCaptainId { get; set; }

        /// <summary>
        /// Return only unread signals.
        /// </summary>
        public bool? UnreadOnly { get; set; }

        /// <summary>
        /// Convert to an EnumerationQuery for database operations.
        /// </summary>
        /// <returns>Populated EnumerationQuery.</returns>
        public EnumerationQuery ToEnumerationQuery()
        {
            EnumerationQuery query = new EnumerationQuery();

            if (PageNumber.HasValue) query.PageNumber = PageNumber.Value;
            if (PageSize.HasValue) query.PageSize = PageSize.Value;

            if (!String.IsNullOrEmpty(Order) && Enum.TryParse<EnumerationOrderEnum>(Order, true, out EnumerationOrderEnum ord))
                query.Order = ord;

            if (!String.IsNullOrEmpty(CreatedAfter) && DateTime.TryParse(CreatedAfter, out DateTime ca))
                query.CreatedAfter = ca;

            if (!String.IsNullOrEmpty(CreatedBefore) && DateTime.TryParse(CreatedBefore, out DateTime cb))
                query.CreatedBefore = cb;

            query.Status = Status;
            query.FleetId = FleetId;
            query.VesselId = VesselId;
            query.CaptainId = CaptainId;
            query.VoyageId = VoyageId;
            query.MissionId = MissionId;
            query.EventType = EventType;
            query.SignalType = SignalType;
            query.ToCaptainId = ToCaptainId;
            query.UnreadOnly = UnreadOnly;

            return query;
        }
    }
}
