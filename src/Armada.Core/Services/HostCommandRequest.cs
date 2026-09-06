namespace Armada.Core.Services
{
    using System.Collections.Generic;

    /// <summary>
    /// A request to run a host command (typically git or gh) in a working directory. Used by the host
    /// executor seam so the Admiral can run the command in-process (Local mode) or delegate it to a Harbor
    /// (Split mode) through the identical shape.
    /// </summary>
    public class HostCommandRequest
    {
        #region Public-Members

        /// <summary>
        /// Executable to run (for example "git" or "gh"). Required.
        /// </summary>
        public string Executable
        {
            get => _Executable;
            set => _Executable = string.IsNullOrWhiteSpace(value) ? "git" : value.Trim();
        }

        /// <summary>
        /// Working directory for the command.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Command-line arguments, in order.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Optional data to write to the command's standard input. Null to leave stdin closed.
        /// </summary>
        public string? StandardInput { get; set; } = null;

        /// <summary>
        /// Timeout in milliseconds. Clamped to a minimum of 0 (0 means no explicit timeout); defaults to
        /// 120000.
        /// </summary>
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private string _Executable = "git";
        private int _TimeoutMs = 120000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HostCommandRequest()
        {
        }

        #endregion
    }
}
