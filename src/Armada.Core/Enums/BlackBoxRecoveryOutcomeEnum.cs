namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What happened after a confirmed black box recovery run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxRecoveryOutcomeEnum
    {
        /// <summary>
        /// The run finished; the mission has not reached a terminal status since.
        /// </summary>
        Pending,

        /// <summary>
        /// The mission reached Complete after the run.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The mission failed, was cancelled or was captured again after the run.
        /// </summary>
        Failed,

        /// <summary>
        /// A step of the run itself failed, so there is no later outcome to watch.
        /// </summary>
        RunFailed,

        /// <summary>
        /// The mission reached no terminal status within the watch window.
        /// </summary>
        Expired,

        /// <summary>
        /// Unrecognized value.
        /// </summary>
        Unknown
    }
}
