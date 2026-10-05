namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// An entity event frame from /ws ({ type, data }) with the identifiers tests match on.
    /// </summary>
    public class E2eWebSocketEventFrame
    {
        #region Public-Members

        /// <summary>
        /// Event type.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Event payload identifiers.
        /// </summary>
        public E2eWebSocketEventData? Data { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the text as an event of exactly this type, or return null when it is another type or does not parse.
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <param name="type">Event type.</param>
        /// <returns>Event or null.</returns>
        public static E2eWebSocketEventFrame? ParseOfType(string? text, string type)
        {
            if (!E2eWebSocketFrame.IsType(text, type)) return null;
            try
            {
                return JsonSerializer.Deserialize<E2eWebSocketEventFrame>(text!, JsonHelper.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
