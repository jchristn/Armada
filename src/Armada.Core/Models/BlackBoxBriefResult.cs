namespace Armada.Core.Models
{
    /// <summary>
    /// A rendered black box brief as returned by the MCP get_blackbox tool (format brief) and the WebSocket
    /// blackbox.brief command; REST returns the Markdown itself as text/markdown.
    /// </summary>
    public class BlackBoxBriefResult
    {
        #region Public-Members

        /// <summary>
        /// Entry the brief was rendered from (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Content type of <see cref="Brief"/> (text/markdown).
        /// </summary>
        public string ContentType { get; set; } = "text/markdown";

        /// <summary>
        /// The Markdown brief (at most 40 KB).
        /// </summary>
        public string Brief { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxBriefResult()
        {
        }

        #endregion
    }
}
