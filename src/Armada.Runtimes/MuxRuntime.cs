namespace Armada.Runtimes
{
    using System;
    using System.Text.Json;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Agent runtime adapter for the Mux CLI.
    /// </summary>
    public class MuxRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public override string Name => "Mux";

        /// <summary>
        /// Mux does not support session resume in Armada's current integration.
        /// </summary>
        public override bool SupportsResume => false;

        /// <summary>
        /// Path to the mux CLI executable.
        /// </summary>
        public string ExecutablePath
        {
            get => _ExecutablePath;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(ExecutablePath));
                _ExecutablePath = value;
            }
        }

        #endregion

        #region Private-Members

        private string _ExecutablePath = "mux";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MuxRuntime(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Get the mux CLI command.
        /// </summary>
        /// <summary>
        /// The runtime this adapter drives.
        /// </summary>
        protected override Armada.Core.Enums.AgentRuntimeEnum RuntimeType => Armada.Core.Enums.AgentRuntimeEnum.Mux;

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Build Mux CLI arguments.
        /// </summary>
        protected override List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain)
        {
            MuxCaptainOptions? options = CaptainRuntimeOptions.GetMuxOptions(captain);
            string? effort = ReasoningEffortTranslator.ToMuxEffort(captain?.ReasoningEffort);
            return MuxCommandBuilder.BuildPrintArguments(workingDirectory, prompt, model, finalMessageFilePath, options, effort, ShowThinking);
        }

        /// <summary>
        /// Determine whether an output line is a Mux structured protocol event (for example
        /// <c>run_started</c> / <c>run_completed</c>) rather than assistant text. Mux emits these as
        /// single-line JSON objects carrying an <c>eventType</c> field; consumers that render the
        /// captain's reply should skip them so the raw protocol JSON does not leak into the chat.
        /// </summary>
        /// <param name="line">A single output line from the Mux CLI.</param>
        /// <returns>True if the line is a Mux protocol event; otherwise false.</returns>
        public static bool IsProtocolEventLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}') return false;

            try
            {
                using (JsonDocument document = JsonDocument.Parse(trimmed))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("eventType", out JsonElement eventType)
                        && eventType.ValueKind == JsonValueKind.String;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Insert launch-plan arguments before the positional prompt (Mux's last argument). When the plan supplies its own
        /// --mcp-config, the config directory's --mcp-config added by the command builder is dropped so only the plan's
        /// (strict) server document is loaded.
        /// </summary>
        /// <param name="args">The runtime's arguments, modified in place.</param>
        /// <param name="extraArguments">The plan's extra arguments.</param>
        protected override void AppendLaunchArguments(List<string> args, List<string> extraArguments)
        {
            if (extraArguments == null || extraArguments.Count == 0) return;

            if (extraArguments.Contains("--mcp-config"))
            {
                int existing = args.IndexOf("--mcp-config");
                if (existing >= 0 && existing + 1 < args.Count - 1) args.RemoveRange(existing, 2);
            }

            int insertAt = args.Count > 0 ? args.Count - 1 : 0;
            args.InsertRange(insertAt, extraArguments);
        }

        #endregion
    }
}
