namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Runbook execution lifecycle states.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RunbookExecutionStatusEnum
    {
        /// <summary>
        /// Execution is currently active.
        /// </summary>
        Running,

        /// <summary>
        /// Execution completed successfully.
        /// </summary>
        Completed,

        /// <summary>
        /// Execution was canceled before completion.
        /// </summary>
        Cancelled
    }
}
