namespace Armada.Core.Models
{
    /// <summary>
    /// Runtime-specific configuration persisted for Mux captains.
    /// </summary>
    public class MuxCaptainOptions
    {
        #region Public-Members

        /// <summary>
        /// Schema version for the serialized options payload.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Optional Mux config directory override.
        /// </summary>
        public string? ConfigDirectory
        {
            get => _ConfigDirectory;
            set => _ConfigDirectory = Normalize(value);
        }

        /// <summary>
        /// Named Mux endpoint to launch and validate against.
        /// </summary>
        public string? Endpoint
        {
            get => _Endpoint;
            set => _Endpoint = Normalize(value);
        }

        /// <summary>
        /// Optional base URL override applied after endpoint selection.
        /// </summary>
        public string? BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = Normalize(value);
        }

        /// <summary>
        /// Optional adapter type override applied after endpoint selection.
        /// </summary>
        public string? AdapterType
        {
            get => _AdapterType;
            set => _AdapterType = Normalize(value);
        }

        /// <summary>
        /// Optional sampling temperature override.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Optional maximum token override.
        /// </summary>
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Optional system prompt file path passed through to Mux.
        /// </summary>
        public string? SystemPromptPath
        {
            get => _SystemPromptPath;
            set => _SystemPromptPath = Normalize(value);
        }

        /// <summary>
        /// Optional approval policy override (ask, auto, deny).
        /// </summary>
        public string? ApprovalPolicy
        {
            get => _ApprovalPolicy;
            set => _ApprovalPolicy = Normalize(value);
        }

        /// <summary>
        /// Runtime-independent auto-approve switch shared with <see cref="CaptainApprovalOptions"/>. When false and no
        /// <see cref="ApprovalPolicy"/> is set, Mux runs with <c>--approval-policy deny</c> instead of <c>--yolo</c>.
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        #endregion

        #region Private-Members

        private string? _ConfigDirectory = null;
        private string? _Endpoint = null;
        private string? _BaseUrl = null;
        private string? _AdapterType = null;
        private string? _SystemPromptPath = null;
        private string? _ApprovalPolicy = null;

        #endregion

        #region Private-Methods

        private static string? Normalize(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return value.Trim();
        }

        #endregion
    }
}
