namespace Armada.Core.Models
{
    /// <summary>
    /// Request body for POST /api/v1/tenants/lookup.
    /// </summary>
    public class TenantLookupRequest
    {
        #region Public-Members

        /// <summary>
        /// Email address to look up.
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

        #endregion

        #region Private-Members

        private string _Email = "";

        #endregion
    }
}
