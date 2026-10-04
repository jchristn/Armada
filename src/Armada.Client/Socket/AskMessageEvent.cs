namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.message: a persisted message.
    /// </summary>
    public class AskMessageEvent
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// The message.
        /// </summary>
        public Armada.Core.Models.AskMessage? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskMessageEvent()
        {
        }

        #endregion
    }
}
