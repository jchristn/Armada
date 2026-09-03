namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
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
        /// Whether a captured output line is an OpenCode JSONL protocol event (a single-line JSON object with
        /// a recognizable event discriminator), so callers can filter it out of human-readable output rather
        /// than leaking raw JSON. Mirrors <c>MuxRuntime.IsProtocolEventLine</c>.
        /// </summary>
        /// <param name="line">Raw output line.</param>
        /// <returns>True when the line is an OpenCode protocol event.</returns>
        public static bool IsProtocolEventLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return false;
            string trimmed = line!.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}') return false;

            try
            {
                using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(trimmed))
                {
                    if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
                    // OpenCode --format json events carry a "type" (and often a "part") discriminator.
                    return document.RootElement.TryGetProperty("type", out _)
                        || document.RootElement.TryGetProperty("part", out _)
                        || document.RootElement.TryGetProperty("eventType", out _);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Best-effort extraction of human-visible assistant text from an OpenCode protocol event, or null
        /// when the event carries no display text (e.g. a tool call or step marker). Used to surface streamed
        /// text without echoing the raw JSON envelope.
        /// </summary>
        /// <param name="line">Raw protocol line.</param>
        /// <returns>The extracted text, or null.</returns>
        public static string? TryExtractAssistantText(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            try
            {
                using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(line!.Trim()))
                {
                    System.Text.Json.JsonElement root = document.RootElement;
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

                    // Reasoning is thinking, not reply text: never surface it as assistant text.
                    string type = root.TryGetProperty("type", out System.Text.Json.JsonElement ty) && ty.ValueKind == System.Text.Json.JsonValueKind.String
                        ? ty.GetString() ?? String.Empty : String.Empty;
                    if (type == "reasoning") return null;

                    if (TryGetString(root, "text", out string? direct)) return direct;
                    if (root.TryGetProperty("part", out System.Text.Json.JsonElement part) && part.ValueKind == System.Text.Json.JsonValueKind.Object
                        && TryGetString(part, "text", out string? partText)) return partText;
                    if (root.TryGetProperty("message", out System.Text.Json.JsonElement message) && message.ValueKind == System.Text.Json.JsonValueKind.Object
                        && TryGetString(message, "content", out string? content)) return content;
                    return null;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static bool TryGetString(System.Text.Json.JsonElement element, string name, out string? value)
        {
            value = null;
            if (element.TryGetProperty(name, out System.Text.Json.JsonElement prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                value = prop.GetString();
                return !String.IsNullOrEmpty(value);
            }
            return false;
        }

        #endregion

        #region Private-Methods

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
            return OpenCodeCommandBuilder.BuildRunArguments(workingDirectory, prompt, model, variant, ShowThinking, AutoApprove);
        }

        /// <summary>
        /// Deliver the prompt on stdin rather than as a positional argument, avoiding the Windows cmd.exe
        /// multi-line-argument truncation. 'opencode run' reads the prompt from stdin when none is supplied.
        /// </summary>
        protected override bool UsePromptStdin => true;

        #endregion
    }
}
