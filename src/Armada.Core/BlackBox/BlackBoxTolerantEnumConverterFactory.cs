namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads enums by name (case-insensitive) or number and writes them by name; a value this build does not know maps to
    /// the enum's Unknown member (or its default when it has none) instead of throwing, and a nullable enum reads an
    /// unknown value as null. Used for record.json so records written by a newer Armada stay readable. Thread-safe.
    /// </summary>
    public class BlackBoxTolerantEnumConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert == null) return false;
            return typeToConvert.IsEnum;
        }

        /// <inheritdoc />
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type converterType = typeof(BlackBoxTolerantEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter?)Activator.CreateInstance(converterType);
        }
    }
}
