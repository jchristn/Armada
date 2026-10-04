namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Live per-thread activity learned from socket events (the dashboard's <c>ThreadActivity</c>): which tracked items
    /// are still active and whether a captain turn is running.
    /// </summary>
    public class AskThreadActivity
    {
        #region Public-Members

        /// <summary>
        /// Tracked work id to active flag. Never null.
        /// </summary>
        public Dictionary<string, bool> Work { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>
        /// A captain turn is running.
        /// </summary>
        public bool Replying { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadActivity()
        {
        }

        #endregion
    }
}
