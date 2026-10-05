namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle state for a release record.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ReleaseStatusEnum
    {
        /// <summary>
        /// Draft release not yet finalized.
        /// </summary>
        Draft,

        /// <summary>
        /// Release candidate ready for final review.
        /// </summary>
        Candidate,

        /// <summary>
        /// Successfully shipped release.
        /// </summary>
        Shipped,

        /// <summary>
        /// Failed release attempt.
        /// </summary>
        Failed,

        /// <summary>
        /// Release was rolled back after shipment.
        /// </summary>
        RolledBack
    }
}
