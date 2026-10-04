namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of browsing a directory for vessel import. When no path was requested, <see cref="Path"/> is null and
    /// <see cref="Entries"/> lists the allowed roots.
    /// </summary>
    public class VesselBrowseResult
    {
        #region Public-Members

        /// <summary>
        /// The directory listed, or null when the allowed roots were listed.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Parent directory when it is still inside an allowed root, otherwise null.
        /// </summary>
        public string? Parent { get; set; } = null;

        /// <summary>
        /// Subdirectories ordered by name. Never null.
        /// </summary>
        public List<VesselBrowseEntry> Entries
        {
            get => _Entries;
            set => _Entries = value ?? new List<VesselBrowseEntry>();
        }

        #endregion

        #region Private-Members

        private List<VesselBrowseEntry> _Entries = new List<VesselBrowseEntry>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselBrowseResult()
        {
        }

        #endregion
    }
}
