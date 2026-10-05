namespace Armada.Tui.Screens.Build
{
    /// <summary>
    /// One line of Workspace terminal output: the kind (command, stdout, stderr, meta) and its text.
    /// </summary>
    public class WorkspaceTerminalLine
    {
        #region Public-Members

        /// <summary>
        /// Kind: <c>command</c>, <c>stdout</c>, <c>stderr</c>, or <c>meta</c>.
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Text (may span lines).
        /// </summary>
        public string Text { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="text">Text.</param>
        public WorkspaceTerminalLine(string kind, string text)
        {
            Kind = kind ?? "meta";
            Text = text ?? "";
        }

        #endregion
    }
}
