namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Core.Protocol;
    using Armada.Core.Services;
    using SyslogLogging;

    /// <summary>
    /// Agent runtime adapter for the OpenCode CLI. OpenCode runs headlessly via <c>opencode run</c>,
    /// speaks OpenAI-compatible providers addressed as <c>provider/model</c>, and can emit raw JSONL
    /// events (<c>--format json</c>) in the same spirit as Mux. Per-captain reasoning effort maps to
    /// OpenCode's <c>--variant</c>, and requesting thinking maps to <c>--thinking</c>.
    /// </summary>
    public class OpenCodeRuntime : BaseAgentRuntime
    {
        #region Public-Members

        /// <summary>
        /// Runtime display name.
        /// </summary>
        public override string Name => "OpenCode";

        /// <summary>
        /// OpenCode session resume is not wired through Armada yet.
        /// </summary>
        public override bool SupportsResume => false;

        /// <summary>
        /// Path to the opencode CLI executable.
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

        /// <summary>
        /// Whether to auto-approve permissions that are not explicitly denied. Required for autonomous
        /// mission execution; without it OpenCode blocks waiting for interactive approval.
        /// </summary>
        public bool AutoApprove { get; set; } = true;

        #endregion

        #region Private-Members

        private string _ExecutablePath = "opencode";
        private readonly OpenCodeFinalMessage _FinalMessage = new OpenCodeFinalMessage();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        public OpenCodeRuntime(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a captured output line is an OpenCode JSONL protocol event (a single-line JSON object whose
        /// <c>type</c> is one of OpenCode's event types, <see cref="OpenCodeStreamEvent.KnownTypes"/>), so callers can filter it out of human-readable output rather
        /// than leaking raw JSON. Mirrors <c>MuxRuntime.IsProtocolEventLine</c>.
        /// </summary>
        /// <param name="line">Raw output line.</param>
        /// <returns>True when the line is an OpenCode protocol event.</returns>
        public static bool IsProtocolEventLine(string? line)
        {
            return OpenCodeStreamEvent.TryParse(line, out OpenCodeStreamEvent? _);
        }

        /// <summary>
        /// Extraction of human-visible assistant text from an OpenCode protocol event, or null when the event carries no
        /// reply text (a tool call, a step marker, or reasoning, which is thinking rather than reply text).
        /// </summary>
        /// <param name="line">Raw protocol line.</param>
        /// <returns>The extracted text, or null.</returns>
        public static string? TryExtractAssistantText(string? line)
        {
            if (!OpenCodeStreamEvent.TryParse(line, out OpenCodeStreamEvent? evt) || evt == null) return null;
            return evt.AssistantText;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Recognize provider failures so a rate limit, an auth failure or an outage is classified instead of reported as a
        /// crash. OpenCode runs with <c>--format json</c> and reports a failed model call as an <c>error</c> event on stdout
        /// (<see cref="RuntimeProviderErrorParser.TryParseOpenCodeJsonLine"/>); stderr lines are read for an HTTP error
        /// status, a Google RPC status or a network error. Text events are the agent's own narration and are never parsed.
        /// </summary>
        /// <param name="line">One output line.</param>
        /// <param name="fromStdout">True for a stdout line.</param>
        /// <returns>The provider error, or null.</returns>
        protected override RuntimeProviderError? TryParseProviderError(string line, bool fromStdout)
        {
            if (fromStdout)
            {
                _FinalMessage.Observe(line);
                return RuntimeProviderErrorParser.TryParseOpenCodeJsonLine(line);
            }
            return RuntimeProviderErrorParser.TryParseCliStderrLine(line, "OpenCode");
        }

        /// <summary>
        /// Start a launch: rebuild its reply from the stdout events for the final-message file.
        /// </summary>
        /// <param name="finalMessageFilePath">The launch's final-message file, or null.</param>
        protected override void BeginLaunch(string? finalMessageFilePath)
        {
            _FinalMessage.Begin(finalMessageFilePath);
        }

        /// <summary>
        /// On a clean exit, write the reply (the text events of the last turn, without reasoning or event JSON) as the
        /// final message, so handoff reads the reply rather than a transcript. Exit 0 after an unrecovered error event is
        /// reported as a failure (<see cref="OpenCodeFinalMessage.Complete"/>).
        /// </summary>
        /// <param name="exitCode">The process exit code, or null when unknown.</param>
        /// <returns>The resolved exit code.</returns>
        protected override int? ResolveExitCode(int? exitCode)
        {
            return _FinalMessage.Complete(exitCode);
        }

        /// <summary>
        /// The runtime this adapter drives.
        /// </summary>
        protected override Armada.Core.Enums.AgentRuntimeEnum RuntimeType => Armada.Core.Enums.AgentRuntimeEnum.OpenCode;

        /// <summary>
        /// Get the command to execute for this runtime.
        /// </summary>
        protected override string GetCommand()
        {
            return ResolveExecutable(_ExecutablePath);
        }

        /// <summary>
        /// Build OpenCode CLI arguments for a headless run.
        /// </summary>
        protected override List<string> BuildArguments(
            string workingDirectory,
            string prompt,
            string? model,
            string? finalMessageFilePath,
            Captain? captain)
        {
            // Per-captain reasoning effort -> OpenCode provider variant (minimal/high).
            string? variant = ReasoningEffortTranslator.ToOpenCodeVariant(captain?.ReasoningEffort);
            return OpenCodeCommandBuilder.BuildRunArguments(workingDirectory, prompt, model, variant, ShowThinking, AutoApprove && CaptainRuntimeOptions.GetAutoApprove(captain));
        }

        /// <summary>
        /// Deliver the prompt on stdin rather than as a positional argument, avoiding the Windows cmd.exe
        /// multi-line-argument truncation. 'opencode run' reads the prompt from stdin when none is supplied.
        /// </summary>
        protected override bool UsePromptStdin => true;

        /// <summary>
        /// Read the host user's OpenCode configuration (global opencode.json / opencode.jsonc / config.json and any
        /// OPENCODE_CONFIG file) and the inherited OPENCODE_CONFIG_CONTENT so a thread-scoped launch can disable the user's
        /// own Armada entries and merge into, rather than replace, inline configuration. Nothing is modified.
        /// </summary>
        /// <param name="request">The plan request to populate.</param>
        protected override void PopulateHostMcpConfiguration(CaptainThreadMcpPlanRequest request)
        {
            List<string> documents = new List<string>();
            string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string globalDirectory = !String.IsNullOrWhiteSpace(xdgConfig)
                ? Path.Combine(xdgConfig, "opencode")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "opencode");
            foreach (string fileName in new string[] { "opencode.jsonc", "opencode.json", "config.json" })
            {
                string? text = TryReadHostFile(Path.Combine(globalDirectory, fileName));
                if (!String.IsNullOrWhiteSpace(text)) documents.Add(text!);
            }

            string? customPath = Environment.GetEnvironmentVariable("OPENCODE_CONFIG");
            string? custom = TryReadHostFile(customPath);
            if (!String.IsNullOrWhiteSpace(custom)) documents.Add(custom!);

            request.HostOpenCodeConfigDocuments = documents;
            request.ExistingOpenCodeConfigContent = Environment.GetEnvironmentVariable("OPENCODE_CONFIG_CONTENT");
        }

        #endregion
    }
}
