namespace Armada.Core.Services
{
    using System;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>
    /// Host context for one usage capture attempt: the tenant the account belongs to, the stable identity
    /// of the credential host the capture runs on, the runtime whose account is probed, and the probe
    /// configuration derived from the group's members. It travels serialized over the Harbor link (the
    /// <c>usageProbeJson</c> field of a <see cref="Harbor.HarborGitRequest"/> with
    /// <see cref="HostCommandKindEnum.UsageProbe"/>), so it carries only a safe profile reference and
    /// configuration, never credential contents: the credential host performs credential access itself.
    /// Deserializing an empty or partially populated context never throws, so an older sender cannot crash
    /// the host helper.
    /// </summary>
    public class UsageProbeHost
    {
        #region Public-Members

        /// <summary>
        /// Owning tenant. Every capture is tenant-scoped; the account key is namespaced by it.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set => _TenantId = value ?? String.Empty;
        }

        /// <summary>
        /// Stable identity of the credential host the capture runs on (the Admiral's host id in Local
        /// mode, the Harbor id in Split mode). Grouping and account identity key on this.
        /// </summary>
        public string HostId
        {
            get => _HostId;
            set => _HostId = value ?? String.Empty;
        }

        /// <summary>
        /// Harbor the capture runs on, when it runs on one; null means the Admiral's own host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Runtime whose account is probed (the group's runtime).
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Capture mode the group's members share: Auto uses only verified read-only sources, Custom runs
        /// the administrator-chosen executable contract, Disabled never reaches the host.
        /// </summary>
        public UsageCaptureModeEnum Mode { get; set; } = UsageCaptureModeEnum.Auto;

        /// <summary>
        /// Custom command configuration JSON (<c>{"executable":"...","arguments":[...]}</c>), or null for
        /// Auto mode. Never shell syntax: the host runs the executable with an argument list.
        /// </summary>
        public string? CommandJson { get; set; } = null;

        /// <summary>
        /// Whether the captain's probe request may fall back to the opt-in PTY scraping source. The host
        /// still refuses it unless the server-wide <c>UsageCapture.AllowPtyFallback</c> switch is set; PTY
        /// is never an automatic fallback.
        /// </summary>
        public bool AllowPty { get; set; } = false;

        /// <summary>
        /// Host execution deadline in seconds. The process tree is terminated on expiry.
        /// </summary>
        public int TimeoutSeconds
        {
            get => _TimeoutSeconds;
            set => _TimeoutSeconds = value < 1 ? 1 : (value > 300 ? 300 : value);
        }

        /// <summary>
        /// Stable discriminator of the probe configuration (runtime, mode and command fingerprint), so a
        /// result is never applied to a group whose configuration changed while it was in flight.
        /// </summary>
        public string? ConfigurationRevision { get; set; } = null;

        /// <summary>
        /// UTC time the Admiral dispatched the attempt, used to bound timestamp skew in the probe's answer.
        /// </summary>
        public DateTime DispatchedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _TenantId = String.Empty;
        private string _HostId = String.Empty;
        private int _TimeoutSeconds = 20;

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageProbeHost()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize to the wire form carried over the Harbor link.
        /// </summary>
        /// <returns>JSON text.</returns>
        public string Serialize()
        {
            return JsonSerializer.Serialize(this, _Options);
        }

        /// <summary>
        /// Deserialize the wire form. Null, empty or malformed JSON becomes an empty context rather than
        /// an exception, so a bad payload cannot crash the host helper or the link.
        /// </summary>
        /// <param name="json">JSON text, or null.</param>
        /// <returns>The host context, or an empty one when it cannot be read.</returns>
        public static UsageProbeHost Deserialize(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return new UsageProbeHost();
            try
            {
                UsageProbeHost? host = JsonSerializer.Deserialize<UsageProbeHost>(json, _Options);
                return host ?? new UsageProbeHost();
            }
            catch (JsonException)
            {
                return new UsageProbeHost();
            }
        }

        #endregion
    }
}