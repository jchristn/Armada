namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// Reads the captain UsageCaptureMode value. An explicit JSON null reads as the default Auto mode; every
    /// other token is read by the enum's own string converter, so unknown names and malformed tokens are still
    /// rejected with a <see cref="JsonException"/>. Writes through the same enum converter (a named string).
    /// </summary>
    public class UsageCaptureModeJsonConverter : JsonConverter<UsageCaptureModeEnum>
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override UsageCaptureModeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return UsageCaptureModeEnum.Auto;
            return Inner(options).Read(ref reader, typeToConvert, options);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, UsageCaptureModeEnum value, JsonSerializerOptions options)
        {
            Inner(options).Write(writer, value, options);
        }

        #endregion

        #region Private-Methods

        private static JsonConverter<UsageCaptureModeEnum> Inner(JsonSerializerOptions options)
        {
            return (JsonConverter<UsageCaptureModeEnum>)options.GetConverter(typeof(UsageCaptureModeEnum));
        }

        #endregion
    }
}
