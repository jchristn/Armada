namespace Armada.Runtimes
{
    using System.Diagnostics;
    using SyslogLogging;
    using Armada.Core.Enums;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Factory for creating agent runtime instances.
    /// </summary>
    public class AgentRuntimeFactory
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[AgentRuntimeFactory] ";
        private LoggingModule _Logging;
        private Dictionary<string, Func<IAgentRuntime>> _CustomRuntimes = new Dictionary<string, Func<IAgentRuntime>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public AgentRuntimeFactory(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an agent runtime by type.
        /// </summary>
        /// <param name="runtimeType">Runtime type.</param>
        /// <returns>Agent runtime instance.</returns>
        public IAgentRuntime Create(AgentRuntimeEnum runtimeType)
        {
            switch (runtimeType)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    return new ClaudeCodeRuntime(_Logging);
                case AgentRuntimeEnum.Codex:
                    return new CodexRuntime(_Logging);
                case AgentRuntimeEnum.Gemini:
                    return new GeminiRuntime(_Logging);
                case AgentRuntimeEnum.Cursor:
                    return new CursorRuntime(_Logging);
                case AgentRuntimeEnum.Custom:
                    throw new InvalidOperationException("Use Create(string name) for custom runtimes");
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtimeType), "Unknown runtime type: " + runtimeType);
            }
        }

        /// <summary>
        /// Create a custom agent runtime by name.
        /// </summary>
        /// <param name="name">Custom runtime name.</param>
        /// <returns>Agent runtime instance.</returns>
        public IAgentRuntime Create(string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (_CustomRuntimes.TryGetValue(name, out Func<IAgentRuntime>? factory))
            {
                return factory();
            }

            throw new InvalidOperationException("No custom runtime registered with name: " + name);
        }

        /// <summary>
        /// Register a custom runtime factory.
        /// </summary>
        /// <param name="name">Custom runtime name.</param>
        /// <param name="factory">Factory function to create the runtime.</param>
        public void Register(string name, Func<IAgentRuntime> factory)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _CustomRuntimes[name] = factory;
            _Logging.Info(_Header + "registered custom runtime: " + name);
        }

        /// <summary>
        /// Validate that a model identifier is usable by the given runtime.
        /// </summary>
        public async Task<string?> ValidateModelAsync(AgentRuntimeEnum runtimeType, string model, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(model)) return null;

            string command;
            List<string> args = new List<string>();

            switch (runtimeType)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    command = "claude";
                    args.Add("--model");
                    args.Add(model);
                    args.Add("--print");
                    args.Add("respond with OK");
                    break;
                case AgentRuntimeEnum.Codex:
                    command = "codex";
                    args.Add("--model");
                    args.Add(model);
                    args.Add("--quiet");
                    args.Add("respond with OK");
                    break;
                default:
                    return null;
            }

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada-validate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempDir);

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = command,
                        WorkingDirectory = tempDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    };

                    foreach (string arg in args)
                        startInfo.ArgumentList.Add(arg);

                    if (runtimeType == AgentRuntimeEnum.ClaudeCode)
                    {
                        startInfo.Environment["CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT"] = "1";
                        startInfo.Environment.Remove("CLAUDECODE");
                        startInfo.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");
                    }

                    Process process = new Process { StartInfo = startInfo };
                    System.Text.StringBuilder stderr = new System.Text.StringBuilder();
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

                    process.Start();
                    process.StandardInput.Close();
                    process.BeginErrorReadLine();

                    bool exited = process.WaitForExit(30000);
                    if (!exited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        return null;
                    }

                    if (process.ExitCode == 0)
                        return null;

                    string errorOutput = stderr.ToString().Trim();
                    if (String.IsNullOrEmpty(errorOutput))
                        errorOutput = "Model validation failed with exit code " + process.ExitCode;
                    return errorOutput;
                }
                finally
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                return "Model validation error: " + ex.Message;
            }
        }

        #endregion
    }
}
