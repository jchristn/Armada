namespace Armada.Tui.Ask
{
    using System.Collections.Generic;

    /// <summary>
    /// A built-in Ask command (the dashboard's <c>AskLocalCommand</c>).
    /// </summary>
    public class AskLocalCommand
    {
        #region Public-Members

        /// <summary>
        /// Which command.
        /// </summary>
        public AskLocalCommandEnum Name { get; set; } = AskLocalCommandEnum.Help;

        /// <summary>
        /// The command with its leading slash.
        /// </summary>
        public string Command { get; set; } = "";

        /// <summary>
        /// Other spellings that run the same command (with their leading slash). Never null.
        /// </summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>
        /// Argument syntax shown after the command in the menu (empty for none). Not translated.
        /// </summary>
        public string Usage { get; set; } = "";

        /// <summary>
        /// Chosen from the menu without arguments, the command fills the composer so the user can type them.
        /// </summary>
        public bool RequiresArgs { get; set; } = false;

        /// <summary>
        /// English title (translated when shown).
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// English description (translated when shown).
        /// </summary>
        public string Description { get; set; } = "";

        #endregion
    }
}
