namespace Armada.Server.RuntimeTools
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// A runtime's built-in (non-MCP) tool list for display in the captain tool viewer. Built on a best-effort basis
    /// from files of the installed runtime package; it is informational only and never drives a decision.
    /// </summary>
    public class RuntimeBuiltInToolInventory
    {
        #region Public-Members

        /// <summary>
        /// Display name of the source (for example "Built-In").
        /// </summary>
        public string SourceName { get; set; } = String.Empty;

        /// <summary>
        /// Display target describing where the list came from.
        /// </summary>
        public string Target { get; set; } = String.Empty;

        /// <summary>
        /// Display note appended to the snapshot summary.
        /// </summary>
        public string Note { get; set; } = String.Empty;

        /// <summary>
        /// Tools in the inventory.
        /// </summary>
        public List<CaptainToolSummary> Tools { get; set; } = new List<CaptainToolSummary>();

        #endregion
    }
}
