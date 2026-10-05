namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A parsed CLI permission rule: the tool name part and the optional specifier inside the parentheses.
    /// </summary>
    public class CliPermissionRulePattern
    {
        #region Public-Members

        /// <summary>
        /// Tool name part (for example Bash, WebFetch, Edit, mcp__server__tool, mcp__server, or mcp__server__*).
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? String.Empty;
        }

        /// <summary>
        /// Specifier inside the parentheses, or null for a bare tool name (and for <c>Tool(*)</c>).
        /// </summary>
        public string? Specifier { get; set; } = null;

        /// <summary>
        /// The rule text as written (trimmed).
        /// </summary>
        public string Raw
        {
            get => _Raw;
            set => _Raw = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _ToolName = String.Empty;
        private string _Raw = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRulePattern()
        {
        }

        #endregion
    }
}
