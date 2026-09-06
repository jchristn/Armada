namespace Armada.Core.Harbor
{
    using System.Collections.Generic;

    /// <summary>
    /// Server-to-Harbor request to launch a captain process. Carries the fully resolved launch plan the
    /// Admiral built (runtime, arguments, environment, working directory, prompt); the Harbor spawns the
    /// process and streams its lifecycle back keyed by <see cref="JobId"/>.
    /// </summary>
    public class HarborLaunchRequest : HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Harbor-scoped job identifier assigned by the server for this launch. All later messages about
        /// the process reference it.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Runtime identifier (for example "claude", "codex", "opencode").
        /// </summary>
        public string Runtime { get; set; } = string.Empty;

        /// <summary>
        /// Absolute working directory (a dock worktree) on the Harbor host.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Optional model identifier to pass to the runtime.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// The prompt to deliver to the captain.
        /// </summary>
        public string? Prompt { get; set; } = null;

        /// <summary>
        /// Whether the prompt is delivered on stdin (true) or as an argument (false).
        /// </summary>
        public bool PromptViaStdin { get; set; } = true;

        /// <summary>
        /// Command-line arguments for the runtime, in order.
        /// </summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// Additional environment variables to set for the process. Secrets should be avoided; the Harbor
        /// uses its own host login for provider auth.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
