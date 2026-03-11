namespace Armada.Core.Services
{
    using System.Diagnostics;
    using Armada.Core.Enums;

    /// <summary>
    /// Detects available agent runtimes by scanning PATH.
    /// </summary>
    public static class RuntimeDetectionService
    {
        #region Public-Methods

        /// <summary>
        /// Detect the first available agent runtime on the system PATH.
        /// Checks in order: claude, codex.
        /// </summary>
        /// <returns>The detected runtime type, or null if none found.</returns>
        public static AgentRuntimeEnum? DetectDefaultRuntime()
        {
            if (IsCommandAvailable("claude")) return AgentRuntimeEnum.ClaudeCode;
            if (IsCommandAvailable("codex")) return AgentRuntimeEnum.Codex;
            if (IsCommandAvailable("gemini")) return AgentRuntimeEnum.Gemini;
            if (IsCommandAvailable("cursor")) return AgentRuntimeEnum.Cursor;
            return null;
        }

        /// <summary>
        /// Detect all available agent runtimes on the system PATH.
        /// </summary>
        /// <returns>List of detected runtime types.</returns>
        public static List<AgentRuntimeEnum> DetectAllRuntimes()
        {
            List<AgentRuntimeEnum> runtimes = new List<AgentRuntimeEnum>();
            if (IsCommandAvailable("claude")) runtimes.Add(AgentRuntimeEnum.ClaudeCode);
            if (IsCommandAvailable("codex")) runtimes.Add(AgentRuntimeEnum.Codex);
            if (IsCommandAvailable("gemini")) runtimes.Add(AgentRuntimeEnum.Gemini);
            if (IsCommandAvailable("cursor")) runtimes.Add(AgentRuntimeEnum.Cursor);
            return runtimes;
        }

        /// <summary>
        /// Check if a command is available on PATH.
        /// </summary>
        /// <param name="command">Command name to check.</param>
        /// <returns>True if the command is found and executable.</returns>
        public static bool IsCommandAvailable(string command)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(startInfo);
                if (process == null) return false;
                process.WaitForExit(5000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get a human-readable install hint for a runtime type.
        /// </summary>
        /// <param name="runtime">Runtime type.</param>
        /// <returns>Install command suggestion.</returns>
        public static string GetInstallHint(AgentRuntimeEnum runtime)
        {
            return runtime switch
            {
                AgentRuntimeEnum.ClaudeCode => "npm install -g @anthropic-ai/claude-code",
                AgentRuntimeEnum.Codex => "npm install -g @openai/codex",
                AgentRuntimeEnum.Gemini => "npm install -g @anthropic-ai/gemini-cli (or see https://github.com/google-gemini/gemini-cli)",
                AgentRuntimeEnum.Cursor => "See https://docs.cursor.com/cli",
                _ => "(see runtime documentation)"
            };
        }

        #endregion
    }
}
