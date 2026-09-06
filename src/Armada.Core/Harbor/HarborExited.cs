namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor-to-server event: a captain process has exited.
    /// </summary>
    public class HarborExited : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifier of the exited process.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Process exit code.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        #endregion
    }
}
