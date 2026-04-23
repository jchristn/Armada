namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Tenant-scoped markdown playbook that can be selected during dispatch.
    /// </summary>
    public class Playbook
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Playbook filename. Expected to end in .md.
        /// </summary>
        public string FileName
        {
            get => _FileName;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(FileName));
                _FileName = value;
            }
        }

        /// <summary>
        /// Optional human-readable description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Markdown content of the playbook.
        /// </summary>
        public string Content
        {
            get => _Content;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Content));
                _Content = value;
            }
        }

        /// <summary>
        /// Whether the playbook is active.
        /// </summary>
        public bool Active { get; set; } = true;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.PlaybookIdPrefix, 24);
        private string _FileName = "PLAYBOOK.md";
        private string _Content = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Playbook()
        {
        }

        /// <summary>
        /// Instantiate with filename and content.
        /// </summary>
        public Playbook(string fileName, string content)
        {
            FileName = fileName;
            Content = content;
        }

        #endregion
    }
}
