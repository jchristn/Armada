namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A built-in quick action offered by the Ask Armada composer, with its argument schema.
    /// </summary>
    public class AskQuickAction
    {
        #region Public-Members

        /// <summary>
        /// Slash command, for example /dispatch.
        /// </summary>
        public string Command
        {
            get => _Command;
            set => _Command = value ?? String.Empty;
        }

        /// <summary>
        /// Display title.
        /// </summary>
        public string Title
        {
            get => _Title;
            set => _Title = value ?? String.Empty;
        }

        /// <summary>
        /// One-line description.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? String.Empty;
        }

        /// <summary>
        /// MCP tool the form submits through POST .../actions.
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Whether the tool is on the read-only allowlist (never needs approval).
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        /// <summary>
        /// Whether the tool requires a tenant admin.
        /// </summary>
        public bool RequiresTenantAdmin { get; set; } = false;

        /// <summary>
        /// Form fields, in display order.
        /// </summary>
        public List<AskQuickActionArgument> Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? new List<AskQuickActionArgument>();
        }

        #endregion

        #region Private-Members

        private string _Command = String.Empty;
        private string _Title = String.Empty;
        private string _Description = String.Empty;
        private string _ToolName = String.Empty;
        private List<AskQuickActionArgument> _Arguments = new List<AskQuickActionArgument>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskQuickAction()
        {
        }

        #endregion
    }
}
