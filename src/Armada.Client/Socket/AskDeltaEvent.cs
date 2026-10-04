namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.chunk and ask.thinking: text appended to a running turn.
    /// </summary>
    public class AskDeltaEvent
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
        /// Appended text.
        /// </summary>
        public string? Delta { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskDeltaEvent()
        {
        }

        #endregion
    }
}
