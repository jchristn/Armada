namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Describes how a project profile was selected for a vessel.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ProjectProfileResolutionModeEnum
    {
        /// <summary>
        /// An explicit project-profile override was requested.
        /// </summary>
        Explicit = 0,

        /// <summary>
        /// A vessel-scoped project profile was selected.
        /// </summary>
        Vessel = 1,

        /// <summary>
        /// A fleet-scoped project profile was selected.
        /// </summary>
        Fleet = 2,

        /// <summary>
        /// A global project profile was selected.
        /// </summary>
        Global = 3,

        /// <summary>
        /// No matching project profile was found.
        /// </summary>
        None = 4
    }
}
