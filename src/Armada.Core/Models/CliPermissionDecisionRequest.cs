namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Body of a decision on a pending CLI permission request (REST, WebSocket, and MCP).
    /// </summary>
    public class CliPermissionDecisionRequest
    {
        #region Public-Members

        /// <summary>
        /// Allow once, allow and remember, or deny.
        /// </summary>
        public CliPermissionDecisionEnum Decision { get; set; } = CliPermissionDecisionEnum.Deny;

        /// <summary>
        /// Optional message: returned to the captain with a denial, recorded with an allow.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Rule pattern for AllowAndRemember (Claude Code syntax); defaults to the request's suggested rule.
        /// </summary>
        public string? RulePattern { get; set; } = null;

        /// <summary>
        /// Rule scope for AllowAndRemember. Default Captain.
        /// </summary>
        public CliPermissionRuleScopeEnum RuleScope { get; set; } = CliPermissionRuleScopeEnum.Captain;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionDecisionRequest()
        {
        }

        #endregion
    }
}
