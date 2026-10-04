namespace Armada.Publisher
{
    /// <summary>
    /// The top-level command the Publisher was invoked to run.
    /// </summary>
    public enum CliCommandEnum
    {
        /// <summary>
        /// No recognized command; print usage.
        /// </summary>
        None,

        /// <summary>
        /// Report packaging-tool readiness for channels runnable on this OS.
        /// </summary>
        Doctor,

        /// <summary>
        /// List declared channels.
        /// </summary>
        List,

        /// <summary>
        /// Run a single named channel.
        /// </summary>
        Channel,

        /// <summary>
        /// Run every enabled channel.
        /// </summary>
        All,

        /// <summary>
        /// Write a SHA256SUMS manifest for the release files in a directory.
        /// </summary>
        Checksums
    }
}
