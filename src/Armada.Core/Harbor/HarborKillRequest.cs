namespace Armada.Core.Harbor
{
    /// <summary>
    /// Server-to-Harbor request to terminate a running captain process, allowing a graceful window before a
    /// forced kill of the process tree.
    /// </summary>
    public class HarborKillRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifier of the target process.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Milliseconds to wait after closing standard input before forcibly killing the process tree.
        /// Clamped to a minimum of 0.
        /// </summary>
        public int GracefulTimeoutMs
        {
            get => _GracefulTimeoutMs;
            set => _GracefulTimeoutMs = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private int _GracefulTimeoutMs = 10000;

        #endregion
    }
}
