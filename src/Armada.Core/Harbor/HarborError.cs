namespace Armada.Core.Harbor
{
    /// <summary>
    /// An error report in either direction: a command could not be carried out, or a job failed outside its
    /// normal exit path. Never carries secrets.
    /// </summary>
    public class HarborError : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job or request identifier the error relates to, or null when not job-specific.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        #endregion
    }
}
