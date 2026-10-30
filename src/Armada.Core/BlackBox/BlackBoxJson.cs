namespace Armada.Core.BlackBox
{
    using System;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Models;

    /// <summary>
    /// JSON conventions of black box objects (record.json, manifest.json): camelCase names, case-insensitive reads,
    /// enums by name, unknown fields ignored and unknown enum values read as Unknown (docs/BLACKBOX_SCHEMA.md,
    /// "Versioning and compatibility"). Thread-safe.
    /// </summary>
    public static class BlackBoxJson
    {
        /// <summary>
        /// Serializer options for black box objects.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = Create(true);

        /// <summary>
        /// Compact serializer options (no indentation), for event payloads.
        /// </summary>
        public static JsonSerializerOptions CompactOptions { get; } = Create(false);

        /// <summary>
        /// Serialize a record to record.json bytes (UTF-8).
        /// </summary>
        /// <param name="record">Record.</param>
        /// <returns>UTF-8 bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public static byte[] SerializeRecord(BlackBoxRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, Options));
        }

        /// <summary>
        /// Deserialize record.json bytes.
        /// </summary>
        /// <param name="data">UTF-8 bytes.</param>
        /// <returns>The record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="JsonException">Thrown when the bytes are not a record.</exception>
        public static BlackBoxRecord DeserializeRecord(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return JsonSerializer.Deserialize<BlackBoxRecord>(data, Options) ?? throw new JsonException("record.json is empty.");
        }

        private static JsonSerializerOptions Create(bool indented)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            options.Converters.Add(new BlackBoxTolerantEnumConverterFactory());
            return options;
        }
    }
}
