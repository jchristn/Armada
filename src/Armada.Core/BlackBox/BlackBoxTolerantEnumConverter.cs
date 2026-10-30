namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tolerant converter for one enum type (see <see cref="BlackBoxTolerantEnumConverterFactory"/>).
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    public class BlackBoxTolerantEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        /// <summary>
        /// Value an unrecognized name or number reads as: the Unknown member when the enum has one, else its default.
        /// </summary>
        public static TEnum Fallback { get; } = Enum.TryParse<TEnum>("Unknown", false, out TEnum unknown) ? unknown : default;

        /// <summary>
        /// Whether the enum has an Unknown member.
        /// </summary>
        public static bool HasUnknown { get; } = Enum.TryParse<TEnum>("Unknown", false, out TEnum _);

        /// <summary>
        /// Parse a name or number; false when this build does not know the value.
        /// </summary>
        /// <param name="reader">Reader positioned on the value.</param>
        /// <param name="value">Parsed value.</param>
        /// <returns>True when known.</returns>
        public static bool TryReadKnown(ref Utf8JsonReader reader, out TEnum value)
        {
            value = default;
            if (reader.TokenType == JsonTokenType.String)
            {
                string? text = reader.GetString();
                if (!String.IsNullOrEmpty(text) && Enum.TryParse<TEnum>(text, true, out TEnum parsed) && Enum.IsDefined(typeof(TEnum), parsed)) { value = parsed; return true; }
                return false;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long number))
            {
                TEnum candidate = (TEnum)Enum.ToObject(typeof(TEnum), number);
                if (Enum.IsDefined(typeof(TEnum), candidate)) { value = candidate; return true; }
                return false;
            }
            reader.Skip();
            return false;
        }

        /// <inheritdoc />
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return TryReadKnown(ref reader, out TEnum value) ? value : Fallback;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.WriteStringValue(value.ToString());
        }
    }
}
