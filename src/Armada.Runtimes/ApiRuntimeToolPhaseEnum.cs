namespace Armada.Runtimes
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Phase of an in-process (ApiEndpoint) runtime tool call.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApiRuntimeToolPhaseEnum
    {
        /// <summary>
        /// The model requested the call and it is about to run.
        /// </summary>
        Started,

        /// <summary>
        /// The call finished (successfully or not).
        /// </summary>
        Completed
    }
}
