namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// A downloaded backup archive.
    /// </summary>
    public class BackupFile
    {
        #region Public-Members

        /// <summary>
        /// Suggested file name.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// Archive bytes. Never null.
        /// </summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BackupFile()
        {
        }

        #endregion
    }
}
