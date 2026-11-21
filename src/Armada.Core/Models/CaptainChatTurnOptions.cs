namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Inputs of one headless captain turn driven by a server-side caller (Ask Armada threads, milestone narration):
    /// the captain, the fully built prompt, the thread-scoped MCP token, and live callbacks.
    /// </summary>
    public class CaptainChatTurnOptions
    {
        #region Public-Members

        /// <summary>
        /// Captain whose runtime runs the turn.
        /// </summary>
        public Captain Captain
        {
            get => _Captain;
            set => _Captain = value ?? throw new ArgumentNullException(nameof(Captain));
        }

        /// <summary>
        /// The complete prompt (system prompt, history, and the new message).
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? throw new ArgumentNullException(nameof(Prompt));
        }

        /// <summary>
        /// Ask the runtime to surface its reasoning. Default false.
        /// </summary>
        public bool ShowThinking { get; set; } = false;

        /// <summary>
        /// Tenant of the caller (token accounting).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User of the caller (token accounting).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Session token that scopes the captain's Armada MCP connection (for example bound to an Ask thread), or null
        /// for no Armada MCP access. ApiEndpoint captains receive it through ARMADA_MCP_URL / ARMADA_MCP_TOKEN; Claude
        /// Code receives a per-launch strict MCP config carrying it as an X-Token header.
        /// </summary>
        public string? McpSessionToken { get; set; } = null;

        /// <summary>
        /// CLI tool permission policy the turn runs with, or null for the captain's legacy behavior. With
        /// ApproveInArmada and a session token, a Claude Code captain sends its permission prompts to Armada's
        /// cli_permission_prompt tool. Bypass and Refuse are applied through the launched captain's auto-approve option.
        /// </summary>
        public Armada.Core.Enums.CliPermissionPolicyEnum? CliPermissionPolicy { get; set; } = null;

        /// <summary>
        /// Seconds a permission prompt may wait for a decision (Permissions.PromptTimeoutSeconds), or null.
        /// </summary>
        public int? PermissionPromptTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Answers an in-process permission prompt (an API-endpoint captain's run_process call under ApproveInArmada):
        /// (tool name, arguments JSON, cancellation) to the outcome; null refuses such calls.
        /// </summary>
        public Func<string, string, System.Threading.CancellationToken, System.Threading.Tasks.Task<CliPermissionPromptOutcome>>? PermissionPrompt { get; set; } = null;

        /// <summary>
        /// Maximum turn duration in milliseconds. Default 300000, minimum 5000, maximum 7200000.
        /// </summary>
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = value < 5000 ? 5000 : (value > 7200000 ? 7200000 : value);
        }

        /// <summary>
        /// Called with each streamed reply delta, or null.
        /// </summary>
        public Action<string>? OnChunk { get; set; } = null;

        /// <summary>
        /// Called with each streamed reasoning delta, or null.
        /// </summary>
        public Action<string>? OnThinking { get; set; } = null;

        /// <summary>
        /// Called with each tool activity event, or null.
        /// </summary>
        public Action<CaptainToolActivity>? OnTool { get; set; } = null;

        /// <summary>
        /// What the turn is about (the Ask conversation and the user's question), sent to a Harbor that runs it for its
        /// Running now list; null for none.
        /// </summary>
        public Armada.Core.Harbor.HarborLaunchDisplay? Display { get; set; } = null;

        /// <summary>
        /// Text to add to the prompt when the turn runs on a Harbor (Harbor id, cancellation), for example where the
        /// tenant's repositories are checked out on that machine, since the turn itself runs in a scratch folder there.
        /// Null or an empty answer adds nothing; an error is ignored.
        /// </summary>
        public Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<string?>>? HarborContext { get; set; } = null;

        /// <summary>
        /// Harbor (hbr_ prefix) the turn must run on, or null to let the launch router choose. When set, the turn runs
        /// on that Harbor or fails (it never falls back to the Admiral host).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Display name of <see cref="HarborId"/> for messages and the prompt, or null to use the identifier.
        /// </summary>
        public string? HarborName { get; set; } = null;

        /// <summary>
        /// Folder the captain runs in on the machine that runs the turn, or null for a temporary scratch folder. A
        /// leading ~ expands to that machine's home directory. On a Harbor a missing folder fails the turn (no scratch
        /// fallback); on the Admiral host it must exist too.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// When true, a short "Where you are running" section (the machine and folder the turn runs in) is added to the
        /// prompt once the launch target is known. Default false.
        /// </summary>
        public bool DescribeRunLocation { get; set; } = false;

        #endregion

        #region Private-Members

        private Captain _Captain = new Captain();
        private string _Prompt = String.Empty;
        private int _TimeoutMs = 300000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatTurnOptions()
        {
        }

        #endregion
    }
}
