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
        /// Checks in order: claude, codex, gemini, cursor-agent, mux, pi. pi is last so adding it never changes the
        /// default on a machine that already has another runtime.
        /// </summary>
        /// <returns>The detected runtime type, or null if none found.</returns>
        public static AgentRuntimeEnum? DetectDefaultRuntime()
        {
            if (IsCommandAvailable("claude")) return AgentRuntimeEnum.ClaudeCode;
            if (IsCommandAvailable("codex")) return AgentRuntimeEnum.Codex;
            if (IsCommandAvailable("gemini")) return AgentRuntimeEnum.Gemini;
            if (IsCommandAvailable("cursor-agent")) return AgentRuntimeEnum.Cursor;
            if (IsCommandAvailable("mux")) return AgentRuntimeEnum.Mux;
            if (IsPiCommand("pi")) return AgentRuntimeEnum.Pi;
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
            if (IsCommandAvailable("cursor-agent")) runtimes.Add(AgentRuntimeEnum.Cursor);
            if (IsCommandAvailable("mux")) runtimes.Add(AgentRuntimeEnum.Mux);
            if (IsPiCommand("pi")) runtimes.Add(AgentRuntimeEnum.Pi);
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
                if (process.WaitForExit(5000)) return true;
                try { process.Kill(true); }
                catch { }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check whether a command is the pi coding agent: it runs and the first line of its <c>--help</c> output starts
        /// with <see cref="PiCommandBuilder.HelpIdentityPrefix"/>. <c>pi</c> is a short name that other tools also use,
        /// and <c>pi --version</c> prints only a bare version, so <c>--help</c> is the identity check.
        /// </summary>
        /// <param name="command">Command name or path, for example "pi".</param>
        /// <returns>True when the command runs and identifies itself as pi.</returns>
        public static bool IsPiCommand(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return false;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--help");

                using Process? process = Process.Start(startInfo);
                if (process == null) return false;
                try { process.StandardInput.Close(); }
                catch { }
                System.Threading.Tasks.Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(true); }
                    catch { }
                    return false;
                }

                string output = stdout.Wait(2000) ? stdout.Result : String.Empty;
                if (PiCommandBuilder.IsPiHelpOutput(output)) return true;
                string errors = stderr.Wait(2000) ? stderr.Result : String.Empty;
                return PiCommandBuilder.IsPiHelpOutput(errors);
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
                AgentRuntimeEnum.Gemini => "npm install -g @google/gemini-cli",
                AgentRuntimeEnum.Cursor => "See https://docs.cursor.com/cli",
                AgentRuntimeEnum.Mux => "Install mux and ensure the `mux` command is on PATH",
                AgentRuntimeEnum.Pi => "Install pi (https://pi.dev) and ensure the `pi` command is on PATH",
                _ => "(see runtime documentation)"
            };
        }

        #endregion
    }
}
