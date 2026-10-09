namespace Armada.Core.Harbor
{
    using System.Collections.Generic;
    using Armada.Core.Services;

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

        /// <summary>
        /// How long the Harbor lets the command run before it kills it, in milliseconds; 0 (the default, and what a
        /// sender that predates the field means) lets it run until it exits.
        /// </summary>
        public int TimeoutMs { get; set; } = 0;

        /// <summary>
        /// Non-zero exit codes the sender treats as an answer, not a failure (for example 1 from <c>git grep</c> for no
        /// match). The Harbor does not report them as failures in its activity log. Null or absent (a sender that
        /// predates the field) means only 0 succeeds.
        /// </summary>
        public List<int>? ExpectedExitCodes { get; set; } = null;

        /// <summary>
        /// What the command is for, so the Harbor can present it: null or absent means routine git work.
        /// </summary>
        public HostCommandKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Optional short name for the command in the Harbor's activity log (for example the check's name), or null.
        /// </summary>
        public string? Label { get; set; } = null;

        #endregion
    }
}
