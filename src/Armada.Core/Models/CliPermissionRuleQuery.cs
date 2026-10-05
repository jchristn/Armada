namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters for listing CLI permission rules (newest first). Every filter is optional.
    /// </summary>
    public class CliPermissionRuleQuery
    {
        #region Public-Members

        /// <summary>
        /// Tenant: rules of this tenant plus the rules that apply to every tenant; null lists every rule.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Scope, or null.
        /// </summary>
        public CliPermissionRuleScopeEnum? Scope { get; set; } = null;

        /// <summary>
        /// Vessel, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionRuleQuery()
        {
        }

        #endregion
    }
}
