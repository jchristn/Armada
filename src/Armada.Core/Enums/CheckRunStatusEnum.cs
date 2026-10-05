namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Current lifecycle state for a check run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CheckRunStatusEnum
    {
        /// <summary>
        /// The check run has been created but not started.
        /// </summary>
        Pending = 0,

        /// <summary>
        /// The check run is currently executing.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The check run completed successfully.
        /// </summary>
        Passed = 2,

        /// <summary>
        /// The check run completed with a failure.
        /// </summary>
        Failed = 3,

        /// <summary>
        /// The check run was canceled before completion.
        /// </summary>
        Canceled = 4
    }
}
