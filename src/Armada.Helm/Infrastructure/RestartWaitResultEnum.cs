namespace Armada.Helm.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of waiting for a remote Admiral to go down and answer again after a restart request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RestartWaitResultEnum
    {
        /// <summary>
        /// The Admiral stopped answering, then answered again.
        /// </summary>
        BackUp,

        /// <summary>
        /// The Admiral stopped answering and did not answer again before the timeout.
        /// </summary>
        DidNotReturn,

        /// <summary>
        /// The Admiral kept answering for the whole timeout; the restart may not have happened.
        /// </summary>
        NeverWentDown
    }
}
