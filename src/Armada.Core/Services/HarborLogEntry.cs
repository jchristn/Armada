namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// A single Harbor link log entry: when it happened, whether it was work coming in from the Admiral or
    /// status going back out, and a human-readable message. Surfaced to the Harbor app's log view so an
    /// operator can watch work being issued and results propagating back over the link.
    /// </summary>
    public class HarborLogEntry
    {
        #region Public-Members

        /// <summary>
        /// When the entry was created (UTC).
        /// </summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The direction of the entry relative to the Harbor.
        /// </summary>
        public HarborLogDirection Direction { get; set; } = HarborLogDirection.Info;

        /// <summary>
        /// The message. Never contains secrets.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLogEntry()
        {
        }

        /// <summary>
        /// Instantiate with a direction and message.
        /// </summary>
        /// <param name="direction">Entry direction.</param>
        /// <param name="message">Entry message.</param>
        public HarborLogEntry(HarborLogDirection direction, string message)
        {
            Direction = direction;
            Message = message ?? string.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render the entry as a single log line: "HH:mm:ss [DIR] message".
        /// </summary>
        /// <returns>The formatted line.</returns>
        public override string ToString()
        {
            string arrow = Direction == HarborLogDirection.In ? "<-" : Direction == HarborLogDirection.Out ? "->" : "  ";
            return TimestampUtc.ToLocalTime().ToString("HH:mm:ss") + " " + arrow + " " + Message;
        }

        #endregion
    }
}
