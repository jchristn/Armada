namespace Armada.Client.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to run an Ask Armada quick action.
    /// </summary>
    public class AskQuickActionRunRequest
    {
        #region Public-Members

        /// <summary>
        /// MCP tool name.
        /// </summary>
        public string ToolName { get; set; } = "";

        /// <summary>
        /// Tool arguments. Never null.
        /// </summary>
        public Dictionary<string, object?> Arguments { get; set; } = new Dictionary<string, object?>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskQuickActionRunRequest()
        {
        }

        #endregion
    }
}
