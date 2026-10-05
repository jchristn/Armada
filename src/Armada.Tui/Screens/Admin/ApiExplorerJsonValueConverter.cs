namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads and writes <see cref="ApiExplorerJsonValue"/>: a string token becomes a string value; any other token is
    /// kept as compact JSON text and written back verbatim (re-indented by the writer).
    /// </summary>
    public class ApiExplorerJsonValueConverter : JsonConverter<ApiExplorerJsonValue>
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Compact = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override ApiExplorerJsonValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string text = reader.GetString() ?? "";
                return new ApiExplorerJsonValue(true, text, JsonSerializer.Serialize(text, _Compact));
            }

            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return new ApiExplorerJsonValue(false, null, JsonSerializer.Serialize(doc.RootElement, _Compact));
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ApiExplorerJsonValue value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            using (JsonDocument doc = JsonDocument.Parse(value.Json))
            {
                doc.RootElement.WriteTo(writer);
            }
        }

        #endregion
    }
}
