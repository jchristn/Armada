namespace Armada.Tui.Ask
{
    /// <summary>
    /// The composer text classified for Enter (the dashboard's <c>AskCommandParse</c>).
    /// </summary>
    public class AskCommandParse
    {
        #region Public-Members

        /// <summary>
        /// Kind.
        /// </summary>
        public AskCommandParseKindEnum Kind { get; set; } = AskCommandParseKindEnum.Text;

        /// <summary>
        /// The command to run (for <see cref="AskCommandParseKindEnum.Command"/>), or null.
        /// </summary>
        public AskCommandItem? Item { get; set; } = null;

        /// <summary>
        /// Arguments after the command, trimmed (empty for none).
        /// </summary>
        public string Args { get; set; } = "";

        /// <summary>
        /// The command word as typed (for <see cref="AskCommandParseKindEnum.Unknown"/>).
        /// </summary>
        public string Typed { get; set; } = "";

        #endregion
    }
}
