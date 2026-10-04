namespace Armada.Client.Models
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads any JSON value into an <see cref="ArmadaRawJson"/> (keeping its text) and writes the raw text back as the
    /// value. Empty or invalid text is written as <c>null</c>. Thread-safe (stateless).
    /// </summary>
    public class ArmadaRawJsonConverter : JsonConverter<ArmadaRawJson>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override ArmadaRawJson? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return new ArmadaRawJson(doc.RootElement.GetRawText());
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ArmadaRawJson value, JsonSerializerOptions options)
        {
            if (value == null || String.IsNullOrWhiteSpace(value.Json))
            {
                writer.WriteNullValue();
                return;
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(value.Json))
                {
                    doc.RootElement.WriteTo(writer);
                }
            }
            catch (JsonException)
            {
                writer.WriteNullValue();
            }
        }

        #endregion
    }
}
