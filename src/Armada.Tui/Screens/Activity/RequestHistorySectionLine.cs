namespace Armada.Tui.Screens.Activity
{
    /// <summary>
    /// One logical line of a <see cref="RequestHistorySectionView"/>: a heading, a label/value pair, plain text, or a
    /// code block line (JSON-highlighted when <see cref="Language"/> is set).
    /// </summary>
    public class RequestHistorySectionLine
    {
        #region Public-Members

        /// <summary>
        /// Kind: "heading", "pair", "text", "muted", or "code".
        /// </summary>
        public string Kind { get; set; } = "text";

        /// <summary>
        /// Text (heading title, pair label, or line text). Already translated.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Secondary text (heading note or pair value). Already translated.
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// Syntax highlighting language for code lines ("json"), or empty.
        /// </summary>
        public string Language { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="text">Text.</param>
        /// <param name="detail">Detail.</param>
        /// <param name="language">Language.</param>
        public RequestHistorySectionLine(string kind, string text, string detail = "", string language = "")
        {
            Kind = kind ?? "text";
            Text = text ?? "";
            Detail = detail ?? "";
            Language = language ?? "";
        }

        #endregion
    }
}
