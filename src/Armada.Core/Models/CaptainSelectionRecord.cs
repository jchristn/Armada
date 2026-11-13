namespace Armada.Core.Models
{
    using System;

    /// <summary>Identity, ownership and lifecycle metadata of a tenant-scoped selection record.</summary>
    public abstract class CaptainSelectionRecord
    {
        /// <summary>PrettyId-generated stable row identity, at most 64 characters.</summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value) || value.Length > 64) throw new ArgumentException("Selection ID must contain 1 to 64 nonblank characters.", nameof(value));
                _Id = value;
            }
        }
        /// <summary>Required tenant boundary; storage rejects absent scope.</summary>
        public string TenantId
        {
            get => _TenantId;
            set
            {
                if (String.IsNullOrWhiteSpace(value) || value.Length > 64) throw new ArgumentException("Tenant ID must contain 1 to 64 nonblank characters.", nameof(value));
                _TenantId = value;
            }
        }
        private string _Id = String.Empty;
        private string _TenantId = String.Empty;
        /// <summary>Resource owner, when applicable; null does not grant end-user access.</summary>
        public string? UserId { get; set; }
        /// <summary>UTC creation time.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        /// <summary>UTC lifecycle update time; immutable history retains its original value.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
        /// <summary>Lifecycle state; never an audit-record edit authorization.</summary>
        public bool Active { get; set; } = true;
        /// <summary>Create a row with its registered entity prefix.</summary>
        protected CaptainSelectionRecord(string prefix) { Id = Constants.IdGenerator.GenerateKSortable(prefix, 24); }
    }
}
