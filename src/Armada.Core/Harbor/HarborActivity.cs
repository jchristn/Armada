namespace Armada.Core.Harbor
{
    using Armada.Core.Models;

    /// <summary>
    /// Harbor-to-server event: the latest activity of a running job (a tool call it started, text it wrote, or
    /// reasoning), read from the runtime's structured output. Sent only for a launch that set
    /// <see cref="HarborLaunchRequest.StructuredProgress"/>, at most about once a second per job (the newest wins).
    /// </summary>
    public class HarborActivity : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Job identifier the activity belongs to.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// The activity.
        /// </summary>
        public RuntimeActivity Activity { get; set; } = new RuntimeActivity();

        #endregion
    }
}
