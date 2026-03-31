namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Status of a batch delete operation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeleteMultipleStatusEnum
    {
        /// <summary>
        /// All requested entities were deleted successfully.
        /// </summary>
        Deleted,

        /// <summary>
        /// Some entities were deleted but others were skipped.
        /// </summary>
        PartiallyDeleted,

        /// <summary>
        /// No entities were deleted (all were skipped or not found).
        /// </summary>
        NoneDeleted
    }
}
