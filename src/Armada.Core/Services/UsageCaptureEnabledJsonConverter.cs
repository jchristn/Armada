namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads the captain UsageCaptureEnabled flag. An explicit JSON null preserves the inherited enabled default;
    /// true and false read as-is and every other token (strings, numbers, objects) is still rejected with a
    /// <see cref="JsonException"/>, so malformed values cannot silently enable capture. Writes the nullable choice.
    /// </summary>
    public class UsageCaptureEnabledJsonConverter : JsonConverter<bool?>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleNull => true;

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
                default:
                    throw new JsonException("UsageCaptureEnabled must be true, false or null.");
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
