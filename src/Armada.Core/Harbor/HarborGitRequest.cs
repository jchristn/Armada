namespace Armada.Core.Harbor
{
    using System.Collections.Generic;

    /// <summary>
    /// Server-to-Harbor request to run a git (or gh) command in a working directory on the Harbor host.
    /// Worktree lifecycle, fetch, commit, push, and PR operations are all expressed as argument lists so
    /// the Admiral does not need direct filesystem access in split mode.
    /// </summary>
    public class HarborGitRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Request identifier tying the result back to this call.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Executable to run, either "git" or "gh". Defaults to "git".
        /// </summary>
        public string Executable { get; set; } = "git";

        /// <summary>
        /// Absolute working directory on the Harbor host.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Command-line arguments, in order.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        #endregion
    }
}
