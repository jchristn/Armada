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

        /// <inheritdoc />
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string? text = reader.GetString();
                if (!String.IsNullOrEmpty(text) && Enum.TryParse<TEnum>(text, true, out TEnum parsed) && Enum.IsDefined(typeof(TEnum), parsed)) return parsed;
                return Fallback;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long number))
            {
                TEnum value = (TEnum)Enum.ToObject(typeof(TEnum), number);
                return Enum.IsDefined(typeof(TEnum), value) ? value : Fallback;
            }
            reader.Skip();
            return Fallback;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            writer.WriteStringValue(value.ToString());
        }
    }
}
