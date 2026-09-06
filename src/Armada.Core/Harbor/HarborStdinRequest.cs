namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor request to write data to a running captain's standard input.
    /// </summary>
    public class HarborStdinRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifier of the target process.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Data to write to standard input.
        /// </summary>
        public string Data { get; set; } = string.Empty;

        #endregion
    }
}
