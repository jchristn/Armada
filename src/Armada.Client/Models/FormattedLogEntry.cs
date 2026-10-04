namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// One entry of a formatted (readable) captain log.
    /// </summary>
    public class FormattedLogEntry
    {
        #region Public-Members

        /// <summary>
        /// Entry text.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// True when the entry is a tool call.
        /// </summary>
        public bool IsToolCall { get; set; } = false;

        /// <summary>
        /// Tool name for tool calls, or null.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// True when content was redacted.
        /// </summary>
        public bool Redacted { get; set; } = false;

        /// <summary>
        /// True when content was truncated.
        /// </summary>
        public bool Truncated { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FormattedLogEntry()
        {
        }

        #endregion
    }
}
