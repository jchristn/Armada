namespace Armada.Client.Models
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// Reads <see cref="RebuildStatus.Status"/>: a <see cref="ServerRebuildStatusEnum"/> name, or null when no rebuild
    /// has run. Servers before the typed field sent the string "none" for that case; it is read as null for wire
    /// compatibility. Any other unknown value is a <see cref="JsonException"/>. Writes the name or null.
    /// </summary>
    public class RebuildStatusValueConverter : JsonConverter<ServerRebuildStatusEnum?>
    {
        #region Public-Members

        /// <summary>
        /// Legacy wire value meaning "no rebuild has run".
        /// </summary>
        public const string LegacyNone = "none";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleNull
        {
            get { return true; }
        }

        /// <inheritdoc />
        public override ServerRebuildStatusEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Rebuild status must be a string or null.");
            string? text = reader.GetString();
            if (String.IsNullOrEmpty(text) || String.Equals(text, LegacyNone, StringComparison.Ordinal)) return null;
            if (EnumNames.TryParse<ServerRebuildStatusEnum>(text, true, out ServerRebuildStatusEnum value)) return value;
            throw new JsonException("Unknown rebuild status '" + text + "'.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ServerRebuildStatusEnum? value, JsonSerializerOptions options)
        {
            if (value == null) writer.WriteNullValue();
            else writer.WriteStringValue(value.Value.ToString());
        }

        #endregion
    }
}
