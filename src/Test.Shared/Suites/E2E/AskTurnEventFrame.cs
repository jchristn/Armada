namespace Test.Shared.Suites.E2E
{
    using System.Text.Json;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Services;

    /// <summary>
    /// A WebSocket <c>ask.turn</c> frame as an E2E test reads it: the event type and its typed payload.
    /// </summary>
    public class AskTurnEventFrame
    {
        #region Public-Members

        /// <summary>
        /// Event type.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Payload (threadId, turnId, state, messageId, error).
        /// </summary>
        public AskTurnEventPayload? Data { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a frame when it is an <c>ask.turn</c> event, otherwise null.
        /// </summary>
        /// <param name="text">Raw frame text.</param>
        /// <returns>The frame, or null.</returns>
        public static AskTurnEventFrame? Parse(string? text)
        {
            if (!E2eWebSocketFrame.IsType(text, "ask.turn")) return null;
            try
            {
                return JsonSerializer.Deserialize<AskTurnEventFrame>(text!, JsonHelper.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
