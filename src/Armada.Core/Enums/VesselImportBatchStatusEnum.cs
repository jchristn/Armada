namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of a vessel import batch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselImportBatchStatusEnum
    {
        /// <summary>
        /// Discovery ran and candidates were recorded; nothing has been imported yet.
        /// </summary>
        Discovered,

        /// <summary>
        /// The import is executing (inline or as a background job).
        /// </summary>
        Importing,

        /// <summary>
        /// Every selected candidate was imported or skipped without failure.
        /// </summary>
        Completed,

        /// <summary>
        /// The import finished but at least one selected candidate failed.
        /// </summary>
        CompletedWithFailures,

        /// <summary>
        /// The import as a whole failed before completing.
        /// </summary>
        Failed
    }
}
