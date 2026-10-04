namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// Payload of ask.thread: a thread changed.
    /// </summary>
    public class AskThreadEvent
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// The thread.
        /// </summary>
        public Armada.Core.Models.AskThread? Thread { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadEvent()
        {
        }

        #endregion
    }
}
