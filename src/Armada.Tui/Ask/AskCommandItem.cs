namespace Armada.Tui.Ask
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// One entry in the composer's <c>/</c> menu: a local command or a quick action (the dashboard's
    /// <c>AskCommandItem</c>).
    /// </summary>
    public class AskCommandItem
    {
        #region Public-Members

        /// <summary>
        /// Unique per catalog (<c>local:new</c>, <c>quick:dispatch</c>).
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// The command with its leading slash.
        /// </summary>
        public string Command { get; set; } = "";

        /// <summary>
        /// Other spellings (with their leading slash). Never null.
        /// </summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>
        /// Argument syntax (empty for none).
        /// </summary>
        public string Usage { get; set; } = "";

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// English description.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Set for a local command.
        /// </summary>
        public AskLocalCommand? Local { get; set; } = null;

        /// <summary>
        /// Set for a quick action.
        /// </summary>
        public AskQuickAction? Action { get; set; } = null;

        #endregion
    }
}
