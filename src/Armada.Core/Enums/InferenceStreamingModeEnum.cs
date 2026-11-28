namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether an API captain endpoint streams model responses.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InferenceStreamingModeEnum
    {
        /// <summary>
        /// Stream on OpenAI-compatible, Ollama, and Ollama Cloud endpoints, and on a Harbor only when it advertises
        /// api-inference-streaming-v1; otherwise do not stream.
        /// </summary>
        [EnumMember(Value = "Automatic")]
        Automatic,

        /// <summary>
        /// Always stream. A Harbor launch requires a Harbor that advertises api-inference-streaming-v1.
        /// </summary>
        [EnumMember(Value = "On")]
        On,

        /// <summary>
        /// Never stream.
        /// </summary>
        [EnumMember(Value = "Off")]
        Off
    }
}
