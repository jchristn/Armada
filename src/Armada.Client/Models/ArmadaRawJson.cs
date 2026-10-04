namespace Armada.Client.Models
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A response whose shape the dashboard treats as untyped (<c>Record&lt;string, unknown&gt;</c> or <c>any</c>). The raw
    /// JSON text is kept so callers can render it (View JSON) or deserialize it into a type they know. Serialized as the
    /// raw JSON value itself (not as an object with a <c>Json</c> property), so it can be sent inside request bodies.
    /// </summary>
    [JsonConverter(typeof(ArmadaRawJsonConverter))]
    public class ArmadaRawJson
    {
        #region Public-Members

        /// <summary>
        /// Raw JSON text. Empty string when the response had no body.
        /// </summary>
        public string Json { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with empty JSON.
        /// </summary>
        public ArmadaRawJson()
        {
        }

        /// <summary>
        /// Instantiate with JSON text.
        /// </summary>
        /// <param name="json">JSON text; null becomes empty.</param>
        public ArmadaRawJson(string? json)
        {
            Json = json ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize the JSON into a type.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <returns>The value, or default when the JSON is empty.</returns>
        /// <exception cref="JsonException">Thrown when the JSON does not match the type.</exception>
        public T? As<T>()
        {
            return ArmadaJson.Deserialize<T>(Json);
        }

        /// <summary>
        /// The JSON indented for display. Returns the raw text when it cannot be parsed.
        /// </summary>
        /// <returns>Indented JSON text.</returns>
        public string ToIndentedString()
        {
            if (String.IsNullOrWhiteSpace(Json)) return "";
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(Json))
                {
                    return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (JsonException)
            {
                return Json;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Json;
        }

        #endregion
    }
}
