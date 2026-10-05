namespace Armada.Proxy.Settings
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a boolean from JSON true/false or the strings "true"/"false" (case-insensitive, for settings files
    /// written by hand). Anything else is a <see cref="JsonException"/>.
    /// </summary>
    public class StrictBooleanJsonConverter : JsonConverter<bool?>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleNull
        {
            get => true;
        }

        /// <inheritdoc />
        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.String:
                    string? text = reader.GetString();
                    if (Boolean.TryParse(text?.Trim(), out bool parsed)) return parsed;
                    throw new JsonException("Expected true or false but found \"" + text + "\".");
                default:
                    throw new JsonException("Expected true or false but found " + reader.TokenType + ".");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteBooleanValue(value.Value);
            else writer.WriteNullValue();
        }

        #endregion
    }
}
