namespace Armada.Client
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON settings shared by the client and socket. Requests are serialized with the server's PascalCase property
    /// names; responses are read case-insensitively with string enums.
    /// </summary>
    public static class ArmadaJson
    {
        #region Public-Members

        /// <summary>
        /// Serializer options. Thread-safe once constructed (read-only use).
        /// </summary>
        public static JsonSerializerOptions Options { get; } = Create();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a value with <see cref="Options"/>.
        /// </summary>
        /// <param name="value">Value; may be null.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(object? value)
        {
            return JsonSerializer.Serialize(value, Options);
        }

        /// <summary>
        /// Deserialize JSON text with <see cref="Options"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text; null or whitespace returns default.</param>
        /// <returns>The value, or default.</returns>
        /// <exception cref="JsonException">Thrown when the text is not valid JSON for the type.</exception>
        public static T? Deserialize<T>(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions Create()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
