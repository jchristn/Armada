namespace Armada.Runtimes.Tools
{
    /// <summary>
    /// Provides configurable safety limits for tool execution.
    /// </summary>
    public static class ToolSafetyLimits
    {
        #region Public-Members

        /// <summary>
        /// The maximum file size in bytes that the read_file tool will accept. Defaults to 1 MB.
        /// </summary>
        public static int MaxReadFileBytes = 1_048_576;

        /// <summary>
        /// The maximum output size in bytes for process stdout/stderr before truncation. Defaults to 100 KB.
        /// </summary>
        public static int MaxProcessOutputBytes = 102_400;

        /// <summary>
        /// The default timeout in milliseconds for general tool execution. Defaults to 30 seconds.
        /// </summary>
        public static int DefaultToolTimeoutMs = 30_000;

        /// <summary>
        /// The default timeout in milliseconds for process execution. Defaults to 120 seconds.
        /// </summary>
        public static int DefaultProcessTimeoutMs = 120_000;

        #endregion
    }
}
