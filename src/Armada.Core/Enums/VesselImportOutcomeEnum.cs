namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of importing a single vessel import candidate.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VesselImportOutcomeEnum
    {
        /// <summary>
        /// The candidate has not been processed yet.
        /// </summary>
        Pending,

        /// <summary>
        /// A vessel was created for the candidate.
        /// </summary>
        Created,

        /// <summary>
        /// The candidate was skipped because a matching vessel already exists.
        /// </summary>
        SkippedExisting,

        /// <summary>
        /// The candidate was skipped because the operator did not select it.
        /// </summary>
        SkippedNotSelected,

        /// <summary>
        /// Creating the vessel failed; see the outcome reason and message.
        /// </summary>
        Failed
    }
}
