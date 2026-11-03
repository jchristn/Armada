namespace Armada.Core.Models
{
    using System;
    using Armada.Core;
    using Armada.Core.Enums;

    /// <summary>
    /// One persisted account usage capture attempt: the limits observed for one account on one host at one
    /// time, sanitized before it reaches storage. Snapshots are derived records: capture and history own
    /// them and retention deletes them; there is no destructive snapshot CRUD. Deduplication is by tenant,
    /// stable host id, runtime and account key (the <see cref="AccountKey"/> HMAC); the snapshot's
    /// <see cref="CaptainId"/> is only the triggering captain, never the owner, and the members the group
    /// covered at capture time are recorded separately as <see cref="UsageSnapshotCaptain"/> links. No
    /// token, credential path, account email, prompt or full HTTP body is ever stored here; only
    /// allowlisted parser diagnostics survive in <see cref="RawExcerpt"/>.
    /// </summary>
    public class UsageSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (usg_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier. Every snapshot belongs to exactly one tenant; lookups and events are
        /// always tenant-scoped and there is no cross-tenant deduplication.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// Captain that triggered this capture attempt, when a specific captain did. Never the owner of the
        /// account: ownership is the account on the host. Null for scheduler ticks or when the triggering
        /// captain was deleted (historic member links are preserved).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Harbor the credential host resolved to, when the capture ran on a Harbor. Null means the Admiral
        /// host itself; the Admiral never falls back to its own credentials when no host could be resolved.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Stable identity of the credential host the capture ran on (Admiral host id or Harbor id).
        /// Grouping and membership key on this, so the same subscription observed from two hosts stays two
        /// distinct, labeled series rather than a fabricated global balance.
        /// </summary>
        public string HostId
        {
            get => _HostId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(HostId));
                _HostId = value;
            }
        }

        /// <summary>
        /// Runtime whose account was probed (for example ClaudeCode, Codex, Custom).
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Provider the account belongs to (for example anthropic, openai), when known. Null means the
        /// source did not report a provider; it is descriptive only and never part of the deduplication key.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque account identity: the provider-issued account identity HMAC-SHA256'd with a host-local
        /// protected key and the tenant namespace. Access tokens are never hashed, so rotating a token does
        /// not create a new account. Null when the identity was unavailable (AuthMissing or Failed before
        /// an identity existed); a null key never merges unrelated captains into an unknown group.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>Admiral-side start of an active capture, before host round trip. Null for legacy/passive observations; those cannot settle post-execution debt.</summary>
        public DateTime? ProbeStartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time the observation was made (the provider's own timestamp for a session-log harvest, the
        /// probe start otherwise). Series group raw samples by this time, not by arrival.
        /// </summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the Admiral accepted and committed the sanitized snapshot.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Capture outcome for this attempt.
        /// </summary>
        public UsageSnapshotStatusEnum Status { get; set; } = UsageSnapshotStatusEnum.Failed;

        /// <summary>
        /// Where the numbers came from.
        /// </summary>
        public UsageCaptureSourceEnum Source { get; set; } = UsageCaptureSourceEnum.OAuthEndpoint;

        /// <summary>
        /// Serialized <see cref="UsageWindow"/> list (usage_snapshots.windows_json). Raw column text; use
        /// <see cref="UsageWindowsJson"/> to read or write it. Null when no windows were observed.
        /// </summary>
        public string? WindowsJson { get; set; } = null;

        /// <summary>
        /// Allowlisted, redacted parser diagnostics capped at the configured excerpt byte limit. Default
        /// empty: no excerpt is stored for a successful capture.
        /// </summary>
        public string? RawExcerpt { get; set; } = null;

        /// <summary>
        /// Stable error code for a failed, unsupported or auth-missing capture (for example
        /// CredentialUnavailable, ProbeTimeout, MalformedOutput, HostUnavailable). Null on success.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.UsageSnapshotIdPrefix, 24);
        private string _TenantId = String.Empty;
        private string _HostId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageSnapshot()
        {
        }

        #endregion
    }
}