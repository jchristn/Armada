namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads the captain UsageCaptureIntervalSeconds value. An explicit JSON null reads as the default interval
    /// (<see cref="DefaultSeconds"/>); integer numbers read as-is (the model setter clamps them); every other
    /// token (strings, fractions, objects) is still rejected with a <see cref="JsonException"/>. Writes a number.
    /// </summary>
    public class UsageCaptureIntervalJsonConverter : JsonConverter<int>
    {
        #region Public-Members

        /// <summary>
        /// The interval an explicit null resolves to.
        /// </summary>
        public const int DefaultSeconds = 300;

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return DefaultSeconds;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value)) return value;
            throw new JsonException("UsageCaptureIntervalSeconds must be an integer or null.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value);
        }

        #endregion
    }
}
