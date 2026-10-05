namespace Armada.Core.Protocol
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Shared deserialization for newline-delimited JSON runtime streams (Claude Code stream-json, Codex exec --json,
    /// Mux, OpenCode). A line is considered only when it is a single JSON object; malformed JSON or a value of the wrong
    /// shape yields false rather than an exception.
    /// </summary>
    public static class RuntimeStreamJson
    {
        #region Public-Methods

        /// <summary>
        /// Try to deserialize one stream line into a typed event.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="line">Raw line.</param>
        /// <param name="value">The event, or null.</param>
        /// <returns>True when the line is a JSON object that deserialized into <typeparamref name="T"/>.</returns>
        public static bool TryDeserializeLine<T>(string? line, out T? value) where T : class
        {
            value = null;
            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line!.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}') return false;

            try
            {
                value = JsonSerializer.Deserialize<T>(trimmed);
            }
            catch (JsonException)
            {
                value = null;
            }
            catch (InvalidOperationException)
            {
                value = null;
            }

            return value != null;
        }

        #endregion
    }
}
