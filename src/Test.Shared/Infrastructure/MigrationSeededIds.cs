namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Identifiers of the rows the migration hygiene suite seeds before replaying migrations, so it can read
    /// each one back afterward.
    /// </summary>
    public sealed class MigrationSeededIds
    {
        /// <summary>
        /// Seeded fleet ID.
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// Seeded vessel ID.
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Seeded captain ID.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Seeded voyage ID.
        /// </summary>
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// Seeded mission ID.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Seeded signal ID.
        /// </summary>
        public string SignalId { get; set; } = "";

        /// <summary>
        /// Seeded event ID.
        /// </summary>
        public string EventId { get; set; } = "";

        /// <summary>
        /// Seeded merge entry ID.
        /// </summary>
        public string MergeEntryId { get; set; } = "";
    }
}
