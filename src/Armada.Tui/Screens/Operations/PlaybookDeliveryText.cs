namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;

    /// <summary>
    /// Playbook delivery mode labels as the dashboard shows them (CamelCase split into words, for example
    /// "Attach Into Worktree").
    /// </summary>
    public static class PlaybookDeliveryText
    {
        #region Public-Methods

        /// <summary>
        /// Label of a delivery mode.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>Label.</returns>
        public static string Format(PlaybookDeliveryModeEnum mode)
        {
            return Regex.Replace(mode.ToString(), "([a-z])([A-Z])", "$1 $2").Trim();
        }

        #endregion
    }
}
