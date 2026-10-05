namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a Claude message <c>content</c> value that is either a string (a plain user prompt) or an array of content
    /// blocks. A string becomes a single <c>text</c> block. Thread-safe (stateless).
    /// </summary>
    public class ClaudeContentListConverter : JsonConverter<List<ClaudeStreamContentBlock>?>
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool HandleNull => true;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override List<ClaudeStreamContentBlock>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String)
            {
                return new List<ClaudeStreamContentBlock>
                {
                    new ClaudeStreamContentBlock { Type = ClaudeStreamContentBlock.TypeText, Text = reader.GetString() }
                };
            }

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Claude message content must be a string or an array of content blocks.");

            return JsonSerializer.Deserialize<List<ClaudeStreamContentBlock>>(ref reader, options);
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, List<ClaudeStreamContentBlock>? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else JsonSerializer.Serialize(writer, value, options);
        }

        #endregion
    }
}
