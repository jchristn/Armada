namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads enums by name (case-insensitive) or number and writes them by name. A value this build does not know maps to
    /// the enum's Unknown member instead of throwing; when the enum has no Unknown member, a nullable enum reads it as null
    /// and a non-nullable one as its default. Used for record.json so records written by a newer Armada stay readable.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxTolerantEnumConverterFactory : JsonConverterFactory
    {
        /// <inheritdoc />
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert == null) return false;
            if (typeToConvert.IsEnum) return true;
            Type? inner = Nullable.GetUnderlyingType(typeToConvert);
            return inner != null && inner.IsEnum;
        }

        /// <inheritdoc />
        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Type? inner = Nullable.GetUnderlyingType(typeToConvert);
            Type converterType = inner != null
                ? typeof(BlackBoxTolerantNullableEnumConverter<>).MakeGenericType(inner)
                : typeof(BlackBoxTolerantEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter?)Activator.CreateInstance(converterType);
        }
    }
}
