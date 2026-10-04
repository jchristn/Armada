namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle of the captain-driven fleet categorization attached to a vessel import batch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselImportCategorizationStatusEnum
    {
        /// <summary>
        /// No categorization was requested for the batch.
        /// </summary>
        None,

        /// <summary>
        /// Categorization was requested and is waiting for the import to finish or for its job to start.
        /// </summary>
        Pending,

        /// <summary>
        /// The captain is analyzing the repositories.
        /// </summary>
        Running,

        /// <summary>
        /// The captain produced fleet recommendations that have not been applied yet.
        /// </summary>
        Completed,

        /// <summary>
        /// Categorization failed or was cancelled; see the categorization error.
        /// </summary>
        Failed,

        /// <summary>
        /// Recommendations were applied: fleets were created or reused and vessels were assigned to them.
        /// </summary>
        Applied
    }
}
