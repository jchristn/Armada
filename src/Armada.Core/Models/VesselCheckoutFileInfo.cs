namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// What a stat of a path in a vessel's checkout found.
    /// </summary>
    public class VesselCheckoutFileInfo
    {
        #region Public-Members

        /// <summary>
        /// Whether the path exists.
        /// </summary>
        public bool Exists { get; set; } = false;

        /// <summary>
        /// Whether the path is a directory.
        /// </summary>
        public bool IsDirectory { get; set; } = false;

        /// <summary>
        /// The file's size in bytes, or null for a directory or a missing path.
        /// </summary>
        public long? SizeBytes { get; set; } = null;

        /// <summary>
        /// When the file was last written, in UTC, or null.
        /// </summary>
        public DateTime? LastWriteUtc { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VesselCheckoutFileInfo()
        {
        }

        #endregion
    }
}
