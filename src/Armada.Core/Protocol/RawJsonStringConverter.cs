namespace Armada.Core.Protocol
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads any JSON value (object, array, string, number, boolean) into a string holding its raw JSON text, and writes
    /// the string back as raw JSON. Used for protocol fields whose shape is open-ended (tool arguments, input schemas,
    /// tool results) so a typed envelope can carry them verbatim without walking a <see cref="JsonElement"/>.
    /// A JSON null reads as null. Thread-safe (stateless).
    /// </summary>
    public class RawJsonStringConverter : JsonConverter<string?>
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return doc.RootElement.GetRawText();
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                writer.WriteNullValue();
                return;
            }

            try
            {
                writer.WriteRawValue(value, skipInputValidation: false);
            }
            catch (JsonException)
            {
                writer.WriteStringValue(value);
            }
        }

        #endregion
    }
}
