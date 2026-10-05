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
        /// The message. Never contains secrets. Never null (null becomes empty). Harbor log lines never end with a
        /// period: surrounding whitespace and a trailing period are removed on set (an ellipsis is kept), which also
        /// covers messages that end with an exception's own text.
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = Normalize(value);
        }

        #endregion

        #region Private-Members

        private string _Message = string.Empty;

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
            Message = message;
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

        #region Private-Methods

        private static string Normalize(string? message)
        {
            string text = (message ?? string.Empty).Trim();
            while (text.EndsWith(".", StringComparison.Ordinal) && !text.EndsWith("...", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).TrimEnd();
            }

            return text;
        }

        #endregion
    }
}
