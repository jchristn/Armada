namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of one GET /api/v1/doctor health check (serialized by name).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DoctorCheckStatusEnum
    {
        /// <summary>
        /// The check passed.
        /// </summary>
        Pass,

        /// <summary>
        /// The check found a non-fatal problem.
        /// </summary>
        Warn,

        /// <summary>
        /// The check failed.
        /// </summary>
        Fail
    }
}
