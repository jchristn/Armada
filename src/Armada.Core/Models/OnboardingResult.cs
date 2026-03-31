namespace Armada.Core.Models
{
    /// <summary>
    /// Result of POST /api/v1/onboarding.
    /// </summary>
    public class OnboardingResult
    {
        #region Public-Members

        /// <summary>
        /// Whether onboarding was successful.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Created tenant metadata.
        /// </summary>
        public TenantMetadata? Tenant { get; set; } = null;

        /// <summary>
        /// Created user (password redacted).
        /// </summary>
        public UserMaster? User { get; set; } = null;

        /// <summary>
        /// Created credential with bearer token.
        /// </summary>
        public Credential? Credential { get; set; } = null;

        /// <summary>
        /// Error message if onboarding failed.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        #endregion
    }
}
