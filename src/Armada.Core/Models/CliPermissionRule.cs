namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A remembered decision for CLI permission prompts, written in Claude Code permission rule syntax (for example
    /// <c>Bash(git status:*)</c>, <c>WebFetch(domain:example.com)</c>, <c>Edit(//repo/src/**)</c>, or a bare tool name).
    /// A matching deny rule denies without prompting; otherwise a matching allow rule allows without prompting.
    /// </summary>
    public class CliPermissionRule
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (cpl_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant the rule applies to; null (global admins only) applies to every tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Scope.
        /// </summary>
        public CliPermissionRuleScopeEnum Scope { get; set; } = CliPermissionRuleScopeEnum.Global;

        /// <summary>
        /// Vessel (vsl_ prefix) for a Vessel rule; null otherwise.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain (cpt_ prefix) for a Captain rule; null otherwise.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Rule in Claude Code permission rule syntax.
        /// </summary>
        public string Pattern
        {
            get => _Pattern;
            set => _Pattern = value ?? String.Empty;
        }

        /// <summary>
        /// Allow or deny.
        /// </summary>
        public CliPermissionRuleActionEnum Action { get; set; } = CliPermissionRuleActionEnum.Allow;

        /// <summary>
        /// Optional note.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// User who created the rule.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.CliPermissionRuleIdPrefix, 24);
        private string _Pattern = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRule()
        {
        }

        #endregion
    }
}
