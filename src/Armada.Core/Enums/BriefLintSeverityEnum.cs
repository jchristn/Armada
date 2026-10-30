namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Severity of a mission brief lint finding. Lint findings never block a dispatch.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BriefLintSeverityEnum
    {
        /// <summary>
        /// Worth a look; the brief may be fine.
        /// </summary>
        Info,

        /// <summary>
        /// Likely to cause a failure (a missing dependency, a no-op mission, a path the vessel does not have).
        /// </summary>
        Warning,

        /// <summary>
        /// Unrecognized value.
        /// </summary>
        Unknown
    }
}
