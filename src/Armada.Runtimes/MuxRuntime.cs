namespace Armada.Runtimes
{
    using System;
    using System.IO;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
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
        /// When set (Approve in Armada), tool calls that need approval are sent to Armada's permission prompt tool on the
        /// scoped "armada" MCP server (<c>mux print --permission-prompt-tool</c>). Null (the default) leaves approval to
        /// the captain's options.
        /// </summary>
        public MuxPermissionPrompt? PermissionPrompt { get; set; } = null;

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
        private string _Header = "[MuxRuntime] ";
        private LoggingModule _Logging;
        private MuxRunOutcomeTracker _Outcome = new MuxRunOutcomeTracker();
        private string? _FinalMessageFilePath = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MuxRuntime(LoggingModule logging) : base(logging)
        {
            _Logging = logging;
        }

        #endregion

        #region Private-Methods

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
            return MuxCommandBuilder.BuildPrintArguments(workingDirectory, prompt, model, finalMessageFilePath, options, effort, ShowThinking, PermissionPrompt);
        }

        /// <summary>
        /// Determine whether an output line is a Mux structured protocol event (for example
        /// <c>run_started</c> / <c>run_completed</c>) rather than assistant text. Mux emits these as
        /// single-line JSON objects whose <c>eventType</c> is one of Mux's event types
        /// (<see cref="MuxProtocolEvent.KnownEventTypes"/>); consumers that render the
        /// captain's reply should skip them so the raw protocol JSON does not leak into the chat.
        /// </summary>
        /// <param name="line">A single output line from the Mux CLI.</param>
        /// <returns>True if the line is a Mux protocol event; otherwise false.</returns>
        public static bool IsProtocolEventLine(string? line)
        {
            return MuxProtocolEvent.TryParse(line, out MuxProtocolEvent? _);
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

        /// <summary>
        /// Start following a new run: its outcome and its final-message file.
        /// </summary>
        /// <param name="finalMessageFilePath">The launch's final-message file, or null.</param>
        protected override void BeginLaunch(string? finalMessageFilePath)
        {
            _Outcome = new MuxRunOutcomeTracker();
            _FinalMessageFilePath = String.IsNullOrEmpty(finalMessageFilePath) ? null : finalMessageFilePath;
        }

        /// <summary>
        /// Read mux's own structured error channel on stdout: each <c>error</c> event, and a failed <c>run_completed</c>
        /// status no error event explained, becomes a provider error with a human-readable reason (see
        /// <see cref="MuxRunOutcomeTracker"/>). Every stdout line is also followed for the run's outcome.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <param name="fromStdout">True for a stdout line.</param>
        /// <returns>The provider error, or null.</returns>
        protected override RuntimeProviderError? TryParseProviderError(string line, bool fromStdout)
        {
            if (!fromStdout) return null;
            return _Outcome.ObserveLine(line);
        }

        /// <summary>
        /// Report mux's error exits (1 after an error event, 2 after a denied tool call) as 0 when the run's
        /// <c>run_completed</c> event says it completed (with or without errors) and no fatal error was reported. Mux
        /// skips --output-last-message on a non-zero exit, so the final reply rebuilt from the stream is written there
        /// when mux wrote nothing.
        /// </summary>
        /// <param name="exitCode">The process exit code, or null when unknown.</param>
        /// <returns>The exit code to report.</returns>
        protected override int? ResolveExitCode(int? exitCode)
        {
            int? resolved = _Outcome.ResolveExitCode(exitCode);
            if (resolved != exitCode)
                _Logging.Info(_Header + "mux exited " + exitCode + " but its run " + (_Outcome.RunStatus ?? "reported a fatal error") + "; reporting exit " + resolved);
            if (resolved == 0 && _Outcome.Succeeded)
            {
                WriteFinalMessageIfMissing(_FinalMessageFilePath, _Outcome.FinalAssistantText);
                // mux writes --output-last-message itself on a clean exit; reasoning an open model left in it is removed.
                if (StripReasoningFromFinalMessage(_FinalMessageFilePath))
                    _Logging.Info(_Header + "removed model reasoning from the final message");
            }
            return resolved;
        }

        /// <summary>
        /// Write a final message rebuilt from the stream to the final-message file when mux left it missing or empty.
        /// Reasoning that leaked into the text (<see cref="ReasoningText"/>) is removed first; nothing is written when only
        /// reasoning remains.
        /// </summary>
        /// <param name="finalMessageFilePath">The final-message file, or null.</param>
        /// <param name="text">The rebuilt final message, or null.</param>
        /// <returns>True when the file was written.</returns>
        public static bool WriteFinalMessageIfMissing(string? finalMessageFilePath, string? text)
        {
            if (String.IsNullOrEmpty(finalMessageFilePath) || String.IsNullOrWhiteSpace(text)) return false;
            text = ReasoningText.Strip(text);
            if (String.IsNullOrWhiteSpace(text)) return false;
            try
            {
                if (File.Exists(finalMessageFilePath) && !String.IsNullOrWhiteSpace(File.ReadAllText(finalMessageFilePath))) return false;
                string? directory = Path.GetDirectoryName(finalMessageFilePath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(finalMessageFilePath, text);
                return true;
            }
            catch (Exception)
            {
                // The final message is optional; callers fall back to the streamed output.
                return false;
            }
        }

        /// <summary>
        /// Remove leaked model reasoning (<see cref="ReasoningText"/>) from a final-message file the CLI wrote itself. A
        /// file that is missing, empty, or holds only reasoning is left as it is.
        /// </summary>
        /// <param name="finalMessageFilePath">The final-message file, or null.</param>
        /// <returns>True when the file was rewritten.</returns>
        public static bool StripReasoningFromFinalMessage(string? finalMessageFilePath)
        {
            if (String.IsNullOrEmpty(finalMessageFilePath)) return false;
            try
            {
                if (!File.Exists(finalMessageFilePath)) return false;
                string text = File.ReadAllText(finalMessageFilePath);
                if (!ReasoningText.HasReasoning(text)) return false;
                string stripped = ReasoningText.Strip(text);
                if (String.IsNullOrWhiteSpace(stripped)) return false;
                File.WriteAllText(finalMessageFilePath, stripped);
                return true;
            }
            catch (Exception)
            {
                // The final message is optional; callers fall back to the streamed output.
                return false;
            }
        }

        #endregion
    }
}
