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

        /// <summary>
        /// Total wall-clock runtime of the job on the Harbor, in milliseconds, or null when not measured.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Time from launch to the first output line (time to first token proxy), in milliseconds, or null
        /// when the job produced no output.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        #endregion
    }
}
