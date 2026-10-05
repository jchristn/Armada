namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.turn: a turn's lifecycle (started, completed, failed, cancelled).
    /// </summary>
    public class AskTurnEvent
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
        /// Turn state, or null when absent.
        /// </summary>
        public AskTurnStateEnum? State { get; set; } = null;

        /// <summary>
        /// Reply message id, or null.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Failure reason, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Failure text under its alternate name (<c>errorText</c>), or null. Read when <see cref="Error"/> is empty.
        /// </summary>
        public string? ErrorText { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskTurnEvent()
        {
        }

        #endregion
    }
}
