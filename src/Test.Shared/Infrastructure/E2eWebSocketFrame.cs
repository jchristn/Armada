namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>
    /// The routing fields of a frame the Admiral sends on /ws (events and command replies): its type, the command
    /// action, and the command error. Parsed from the frame text so tests match the type exactly instead of searching
    /// the frame for a type name.
    /// </summary>
    public class E2eWebSocketFrame
    {
        #region Public-Members

        /// <summary>
        /// Frame type, for example "status.snapshot", "command.result", "voyage.changed".
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Command action for command.result and command.error frames, or null.
        /// </summary>
        public string? Action { get; set; } = null;

        /// <summary>
        /// Command error text for command.error frames, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Machine-readable reason for command.error frames, or null.
        /// </summary>
        public WebSocketCommandErrorCodeEnum? Code { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse a frame, or return null when the text is not a JSON object.
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <returns>Frame or null.</returns>
        public static E2eWebSocketFrame? Parse(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonSerializer.Deserialize<E2eWebSocketFrame>(text, JsonHelper.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the text is a frame of exactly this type.
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <param name="type">Frame type.</param>
        /// <returns>True on match.</returns>
        public static bool IsType(string? text, string type)
        {
            E2eWebSocketFrame? frame = Parse(text);
            return frame != null && String.Equals(frame.Type, type, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the text is a command reply (command.result or command.error).
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <returns>True for a command reply.</returns>
        public static bool IsCommandReply(string? text)
        {
            return IsType(text, "command.result") || IsType(text, "command.error");
        }

        #endregion
    }
}
