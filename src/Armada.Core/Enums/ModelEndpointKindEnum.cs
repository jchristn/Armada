namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The kind of model an endpoint serves, which selects how it is validated (an embedding request vs a
    /// completion request).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelEndpointKindEnum
    {
        /// <summary>
        /// An embedding model endpoint: text in, a vector out. Validated with an embedding request.
        /// </summary>
        [EnumMember(Value = "Embedding")]
        Embedding,

        /// <summary>
        /// An inference (chat/completion) model endpoint. Validated with a completion request.
        /// </summary>
        [EnumMember(Value = "Inference")]
        Inference
    }
}
