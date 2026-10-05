namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Describes how a workflow profile was selected for a vessel preview.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkflowProfileResolutionModeEnum
    {
        /// <summary>
        /// An explicit workflow-profile override was requested.
        /// </summary>
        Explicit = 0,

        /// <summary>
        /// A vessel-scoped workflow profile was selected.
        /// </summary>
        Vessel = 1,

        /// <summary>
        /// A fleet-scoped workflow profile was selected.
        /// </summary>
        Fleet = 2,

        /// <summary>
        /// A global workflow profile was selected.
        /// </summary>
        Global = 3,
    }
}
