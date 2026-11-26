namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a context window value came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContextLengthSourceEnum
    {
        /// <summary>
        /// An operator set it on the endpoint (InferenceOptions.ContextLength). A configured value always wins.
        /// </summary>
        [EnumMember(Value = "Configured")]
        Configured,

        /// <summary>
        /// Armada read it from the model server (Ollama /api/ps or /api/show, an OpenAI-compatible /v1/models) because
        /// the endpoint configures none.
        /// </summary>
        [EnumMember(Value = "Discovered")]
        Discovered,

        /// <summary>
        /// A model probe run measured it.
        /// </summary>
        [EnumMember(Value = "Measured")]
        Measured
    }
}
