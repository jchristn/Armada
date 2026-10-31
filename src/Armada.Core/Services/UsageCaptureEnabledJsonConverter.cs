namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads the captain UsageCaptureEnabled flag. An explicit JSON null reads as the disabled default (false);
    /// true and false read as-is and every other token (strings, numbers, objects) is still rejected with a
    /// <see cref="JsonException"/>, so malformed values cannot silently enable capture. Writes a boolean.
    /// </summary>
    public class UsageCaptureEnabledJsonConverter : JsonConverter<bool>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return false;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                default:
                    throw new JsonException("UsageCaptureEnabled must be true, false or null.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        {
            writer.WriteBooleanValue(value);
        }

        #endregion
    }
}
