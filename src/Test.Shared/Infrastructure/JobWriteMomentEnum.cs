namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which worker write of a job a <see cref="JobCancelInjector"/> lands its cancel in front of.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum JobWriteMomentEnum
    {
        /// <summary>
        /// The worker's start transition: a write that sets Running on a job stored as Queued.
        /// </summary>
        Start,

        /// <summary>
        /// A heartbeat or progress write on a Running job: a write that sets no status, or sets Running on a job
        /// already stored as Running.
        /// </summary>
        Heartbeat
    }
}
