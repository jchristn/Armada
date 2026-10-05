namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reads <see cref="ApiExplorerSchemaTypes"/> from a string or an array of strings (null entries skipped). Any
    /// other token is a <see cref="JsonException"/>. Writes a string for one name, else an array.
    /// </summary>
    public class ApiExplorerSchemaTypesConverter : JsonConverter<ApiExplorerSchemaTypes>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override ApiExplorerSchemaTypes? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ApiExplorerSchemaTypes result = new ApiExplorerSchemaTypes();
            if (reader.TokenType == JsonTokenType.String)
            {
                string? name = reader.GetString();
                if (!String.IsNullOrEmpty(name)) result.Names.Add(name!);
                return result;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                List<string?>? names = JsonSerializer.Deserialize<List<string?>>(ref reader, options);
                if (names != null)
                {
                    foreach (string? name in names)
                    {
                        if (!String.IsNullOrEmpty(name)) result.Names.Add(name!);
                    }
                }

                return result;
            }

            throw new JsonException("Schema type must be a string or an array of strings.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ApiExplorerSchemaTypes value, JsonSerializerOptions options)
        {
            if (value == null || value.Names.Count == 0)
            {
                writer.WriteNullValue();
                return;
            }

            if (value.Names.Count == 1)
            {
                writer.WriteStringValue(value.Names[0]);
                return;
            }

            writer.WriteStartArray();
            foreach (string name in value.Names) writer.WriteStringValue(name);
            writer.WriteEndArray();
        }

        #endregion
    }
}
