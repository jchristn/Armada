namespace Test.Shared.Suites.Services
{
    using System.Text.Json;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// The ask.turn event payload (threadId, turnId, state, messageId, error), read back from the recorded object.
    /// </summary>
    public class AskTurnEventPayload
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Turn id.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// Turn state: started, completed, failed, cancelled.
        /// </summary>
        public string? State { get; set; } = null;

        /// <summary>
        /// Message id, or null.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Error text, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Read a recorded payload object (an anonymous object in production) as this type.
        /// </summary>
        /// <param name="payload">Recorded payload.</param>
        /// <returns>Typed payload.</returns>
        public static AskTurnEventPayload From(object? payload)
        {
            return JsonHelper.Deserialize<AskTurnEventPayload>(JsonSerializer.Serialize(payload));
        }

        #endregion
    }
}
