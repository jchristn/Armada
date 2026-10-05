namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a JSON object into a case-insensitive string dictionary, keeping members whose value is a JSON string and
    /// skipping members of any other type (as runtime config files may carry them), so one odd value does not make the
    /// whole config unreadable. A non-object value reads as null.
    /// </summary>
    public class StringValuesDictionaryConverter : JsonConverter<Dictionary<string, string>?>
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Dictionary<string, string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return result;
                if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException("Expected a property name.");

                string name = reader.GetString() ?? String.Empty;
                if (!reader.Read()) throw new JsonException("Unexpected end of JSON.");

                if (reader.TokenType == JsonTokenType.String)
                {
                    result[name] = reader.GetString() ?? String.Empty;
                }
                else
                {
                    reader.Skip();
                }
            }

            throw new JsonException("Unexpected end of JSON.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, Dictionary<string, string>? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> entry in value)
            {
                writer.WriteString(entry.Key, entry.Value);
            }

            writer.WriteEndObject();
        }

        #endregion
    }
}
