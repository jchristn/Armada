namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tolerant converter for a nullable enum: null reads as null; an unknown value reads as the Unknown member, or null
    /// when the enum has none (see <see cref="BlackBoxTolerantEnumConverterFactory"/>).
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    public class BlackBoxTolerantNullableEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
    {
        /// <inheritdoc />
        public override bool HandleNull => true;

        /// <inheritdoc />
        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (BlackBoxTolerantEnumConverter<TEnum>.TryReadKnown(ref reader, out TEnum value)) return value;
            return BlackBoxTolerantEnumConverter<TEnum>.HasUnknown ? BlackBoxTolerantEnumConverter<TEnum>.Fallback : (TEnum?)null;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (value.HasValue) writer.WriteStringValue(value.Value.ToString());
            else writer.WriteNullValue();
        }
    }
}
