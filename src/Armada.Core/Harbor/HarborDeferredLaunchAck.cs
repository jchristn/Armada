namespace Armada.Core.Harbor
{
    /// <summary>
    /// Harbor acknowledgement of a <see cref="HarborDeferredLaunchRequest"/>. Confirms the instruction is armed
    /// so the Admiral can safely exit knowing the Harbor will perform the cutover. See docs/SERVER_REBUILD.md.
    /// </summary>
    public class HarborDeferredLaunchAck : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Correlation id echoing the request. Required.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Whether the Harbor armed the deferred launch. False indicates it could not (for example a missing
        /// executable), in which case the Admiral should fall back to the in-process baton.
        /// </summary>
        public bool Armed { get; set; } = false;

        /// <summary>
        /// Optional diagnostic detail when <see cref="Armed"/> is false.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborDeferredLaunchAck()
        {
        }

        #endregion
    }
}
