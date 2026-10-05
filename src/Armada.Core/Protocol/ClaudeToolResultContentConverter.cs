namespace Armada.Core.Protocol
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads a Claude <c>tool_result</c> content value that is either a string or an array of content blocks; any other
    /// JSON value is kept as its raw JSON text. Writes the text as a string. Thread-safe (stateless).
    /// </summary>
    public class ClaudeToolResultContentConverter : JsonConverter<ClaudeToolResultContent>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override ClaudeToolResultContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String) return new ClaudeToolResultContent { Text = reader.GetString() ?? String.Empty };

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                List<ClaudeStreamContentBlock>? blocks = JsonSerializer.Deserialize<List<ClaudeStreamContentBlock>>(ref reader, options);
                StringBuilder builder = new StringBuilder();
                if (blocks != null)
                {
                    foreach (ClaudeStreamContentBlock block in blocks)
                    {
                        if (block == null || block.Text == null) continue;
                        if (builder.Length > 0) builder.Append('\n');
                        builder.Append(block.Text);
                    }
                }

                return new ClaudeToolResultContent { Text = builder.ToString() };
            }

            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return new ClaudeToolResultContent { Text = doc.RootElement.GetRawText() };
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ClaudeToolResultContent value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value.Text);
        }

        #endregion
    }
}
