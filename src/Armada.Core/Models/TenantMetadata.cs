namespace Armada.Core.Models
{
    /// <summary>
    /// Represents a tenant in the multi-tenant system.
    /// </summary>
    public class TenantMetadata
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (prefix: ten_).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Whether the tenant is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether the tenant is protected from direct deletion.
        /// </summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.TenantIdPrefix, 24);
        private string _Name = "My Tenant";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TenantMetadata()
        {
        }

        /// <summary>
        /// Instantiate with name.
        /// </summary>
        /// <param name="name">Tenant name.</param>
        public TenantMetadata(string name)
        {
            Name = name;
        }

        #endregion
    }
}
