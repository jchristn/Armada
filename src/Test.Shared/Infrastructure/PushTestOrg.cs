namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Users of one tenant (plus one user of another tenant) for push recipient tests.
    /// </summary>
    public sealed class PushTestOrg
    {
        #region Public-Members

        /// <summary>Tenant.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Regular user who owns the entities.</summary>
        public string Owner { get; set; } = String.Empty;

        /// <summary>Tenant admin.</summary>
        public string TenantAdmin { get; set; } = String.Empty;

        /// <summary>Global admin whose home tenant is this tenant.</summary>
        public string GlobalAdmin { get; set; } = String.Empty;

        /// <summary>Regular user who owns nothing.</summary>
        public string Bystander { get; set; } = String.Empty;

        /// <summary>Inactive tenant admin.</summary>
        public string Inactive { get; set; } = String.Empty;

        /// <summary>Regular user of another tenant.</summary>
        public string ForeignUser { get; set; } = String.Empty;

        #endregion
    }
}
