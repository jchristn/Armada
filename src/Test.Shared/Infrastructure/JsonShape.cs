namespace Test.Shared.Infrastructure
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Reads the property names and value tokens of a JSON document with <see cref="Utf8JsonReader"/>. For wire-shape
    /// checks only (absent property, explicit null, number vs string); assert values on a deserialized model instead.
    /// </summary>
    public static class JsonShape
    {
        #region Public-Methods

        /// <summary>
        /// Every property at any depth, in document order.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Properties.</returns>
        /// <exception cref="JsonException">Thrown when the text is not valid JSON.</exception>
        public static List<JsonPropertyShape> Properties(string json)
        {
            List<JsonPropertyShape> result = new List<JsonPropertyShape>();
            if (String.IsNullOrWhiteSpace(json)) return result;
            Utf8JsonReader reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                JsonPropertyShape shape = new JsonPropertyShape();
                shape.Name = reader.GetString() ?? "";
                shape.Depth = reader.CurrentDepth;
                reader.Read();
                shape.ValueToken = reader.TokenType;
                if (reader.TokenType == JsonTokenType.String) shape.ScalarText = reader.GetString();
                else if (reader.TokenType == JsonTokenType.Number) shape.ScalarText = Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray());
                result.Add(shape);
            }

            return result;
        }

        /// <summary>
        /// Properties of the top-level object only.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Properties.</returns>
        public static List<JsonPropertyShape> TopLevel(string json)
        {
            return Properties(json).Where(p => p.Depth == 1).ToList();
        }

        /// <summary>
        /// The top-level property with a name (case-insensitive), or null when absent.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <param name="name">Property name.</param>
        /// <returns>Shape or null.</returns>
        public static JsonPropertyShape? TopLevelProperty(string json, string name)
        {
            return TopLevel(json).FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True when a property with this name (case-insensitive) appears anywhere in the document.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <param name="name">Property name.</param>
        /// <returns>True when present at any depth.</returns>
        public static bool HasPropertyAnywhere(string json, string name)
        {
            return Properties(json).Any(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
