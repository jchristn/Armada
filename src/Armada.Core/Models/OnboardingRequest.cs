namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for POST /api/v1/onboarding (self-registration).
    /// </summary>
    public class OnboardingRequest
    {
        #region Public-Members

        /// <summary>
        /// Tenant identifier to join.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// User email address.
        /// </summary>
        public string Email
        {
            get => _Email;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Email));
                _Email = value;
            }
        }

        /// <summary>
        /// User password (plaintext).
        /// </summary>
        public string Password
        {
            get => _Password;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Password));
                _Password = value;
            }
        }

        /// <summary>
        /// Optional first name.
        /// </summary>
        public string? FirstName { get; set; } = null;

        /// <summary>
        /// Optional last name.
        /// </summary>
        public string? LastName { get; set; } = null;

        #endregion

        #region Private-Members

        private string _TenantId = "";
        private string _Email = "";
        private string _Password = "";

        #endregion
    }
}
