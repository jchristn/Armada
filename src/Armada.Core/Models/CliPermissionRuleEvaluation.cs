namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Result of evaluating the applicable rules against one tool call: the action of the deciding rule, or null when no
    /// rule decides (the call is then prompted).
    /// </summary>
    public class CliPermissionRuleEvaluation
    {
        #region Public-Members

        /// <summary>
        /// Allow or Deny, or null when no rule decides.
        /// </summary>
        public CliPermissionRuleActionEnum? Action { get; set; } = null;

        /// <summary>
        /// The deciding rule, or null.
        /// </summary>
        public CliPermissionRule? Rule { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRuleEvaluation()
        {
        }

        #endregion
    }
}
