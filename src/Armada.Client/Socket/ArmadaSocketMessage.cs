namespace Armada.Client.Socket
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// One message received on the Armada WebSocket: <c>{ type, message?, data?, timestamp }</c>. The data payload is
    /// kept as a JSON node and converted to its typed payload class on demand with <see cref="GetData{T}"/> or
    /// <see cref="GetTypedData"/>.
    /// </summary>
    public class ArmadaSocketMessage
    {
        #region Public-Members

        /// <summary>
        /// Event type, for example <c>mission.changed</c> (see <see cref="ArmadaEventTypes"/>). Empty when absent.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Optional human-readable message, or null.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Server timestamp, or null.
        /// </summary>
        public DateTime? Timestamp { get; set; } = null;

        /// <summary>
        /// Raw payload, or null.
        /// </summary>
        public JsonNode? Data { get; set; } = null;

        /// <summary>
        /// Raw JSON text of the whole message.
        /// </summary>
        public string RawJson { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ArmadaSocketMessage()
        {
        }

        /// <summary>
        /// Parse a message from JSON text. Returns null for text that is not a JSON object with a type.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The message, or null.</returns>
        public static ArmadaSocketMessage? Parse(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                ArmadaSocketMessage? message = JsonSerializer.Deserialize<ArmadaSocketMessage>(json!, ArmadaJson.Options);
                if (message == null || String.IsNullOrEmpty(message.Type)) return null;
                message.RawJson = json!;
                return message;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert the payload to a type. Returns default when there is no payload or it does not match.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <returns>The payload, or default.</returns>
        public T? GetData<T>()
        {
            if (Data == null) return default;
            try
            {
                return Data.Deserialize<T>(ArmadaJson.Options);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        /// <summary>
        /// Convert the payload to the class registered for <see cref="Type"/> in <see cref="ArmadaEventTypes.PayloadTypes"/>.
        /// Returns null for unknown types, missing payloads, or payloads that do not match.
        /// </summary>
        /// <returns>The typed payload, or null.</returns>
        public object? GetTypedData()
        {
            Type? type = ArmadaEventTypes.PayloadTypeFor(Type);
            if (type == null || Data == null) return null;
            try
            {
                return Data.Deserialize(type, ArmadaJson.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
