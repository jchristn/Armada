namespace Armada.Core.Services
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a number, a numeric string, or any other token (for example "Unknown") as a nullable double;
    /// non-numeric values read as null. Writes numbers or null.
    /// </summary>
    public class LenientNullableDoubleConverter : JsonConverter<double?>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.TryGetDouble(out double number) ? number : null;
                case JsonTokenType.String:
                    string? text = reader.GetString();
                    return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    reader.Skip();
                    return null;
                default:
                    return null;
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }

        #endregion
    }
}
