namespace Armada.Runtimes.Mcp
{
    using System;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a JSON-RPC <c>id</c> (a number, a string, or null) as its canonical string form so a response can be matched
    /// to the request that produced it regardless of how the peer encoded the id. Writes the value back as a string.
    /// </summary>
    public class JsonRpcIdConverter : JsonConverter<string?>
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long whole)) return whole.ToString(CultureInfo.InvariantCulture);
                    return reader.GetDouble().ToString("R", CultureInfo.InvariantCulture);
                default:
                    throw new JsonException("A JSON-RPC id must be a string, a number, or null.");
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value);
        }

        #endregion
    }
}
