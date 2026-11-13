namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// A black box entry download (GET /api/v1/blackbox/{id}/download): the zip of every object plus manifest.json and
    /// the server's suggested file name.
    /// </summary>
    public class BlackBoxDownload
    {
        #region Public-Members

        /// <summary>
        /// Suggested file name ({entryId}.zip).
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// Zip bytes.
        /// </summary>
        public byte[] Content { get; set; } = Array.Empty<byte>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDownload()
        {
        }

        #endregion
    }
}
