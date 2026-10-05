namespace Armada.Core.Protocol
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a JSON string as its string value and any other JSON value as its raw JSON text. Used for protocol fields
    /// that are usually text but may be structured (for example a tool's output). Writes a string value. Thread-safe.
    /// </summary>
    public class TextOrRawJsonConverter : JsonConverter<string?>
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
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return doc.RootElement.GetRawText();
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }

        #endregion
    }
}
