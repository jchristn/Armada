namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// One account usage capture group: the set of enabled captains that share one probe configuration
    /// (tenant, credential host, runtime and, for Custom, the command fingerprint), so they share one
    /// attempt, one snapshot and one member link list per capture. The group's effective interval is the
    /// shortest interval its members requested, floored by the server-wide minimum; the account key is
    /// discovered by the observation and is never guessed in advance, so a captain with a missing
    /// identity is never merged into an unknown group.
    /// </summary>
    public class UsageCaptureGroup
    {
        #region Public-Members

        /// <summary>
        /// Stable group key (tenant, host, runtime, command fingerprint).
        /// </summary>
        public string GroupKey
        {
            get => _GroupKey;
            set => _GroupKey = value ?? String.Empty;
        }

        /// <summary>
        /// Owning tenant.
        /// </summary>
        public string TenantId
        {
            get => _TenantId;
            set => _TenantId = value ?? String.Empty;
        }

        /// <summary>
        /// Stable identity of the credential host the group is probed on.
        /// </summary>
        public string HostId
        {
            get => _HostId;
            set => _HostId = value ?? String.Empty;
        }

        /// <summary>
        /// Harbor that hosts the credentials, when the group runs on one; null on the Admiral's host.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Runtime whose account the group probes.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Capture mode the group's members share.
        /// </summary>
        public UsageCaptureModeEnum Mode { get; set; } = UsageCaptureModeEnum.Auto;

        /// <summary>
        /// Custom command configuration JSON shared by the group, or null for Auto.
        /// </summary>
        public string? CommandJson { get; set; } = null;

        /// <summary>
        /// Fingerprint of the Custom command configuration, or null for Auto. Part of the group key, so
        /// two different executable identities never share results.
        /// </summary>
        public string? CommandFingerprint { get; set; } = null;

        /// <summary>
        /// Probe configuration revision (host, runtime, command fingerprint). A result whose revision no
        /// longer matches is discarded.
        /// </summary>
        public string? ConfigurationRevision { get; set; } = null;

        /// <summary>
        /// Shortest capture interval the members requested, in seconds, before the global floor.
        /// </summary>
        public int RequestedIntervalSeconds { get; set; } = 300;

        /// <summary>
        /// Effective capture interval: the shortest requested interval floored by the server-wide
        /// minimum.
        /// </summary>
        public int EffectiveIntervalSeconds { get; set; } = 300;

        /// <summary>
        /// Whether the opt-in PTY source may be used for this group (the server-wide switch).
        /// </summary>
        public bool AllowPty { get; set; } = false;

        /// <summary>
        /// Members: the enabled captains of this group at capture time. Never null.
        /// </summary>
        public List<string> MemberCaptainIds
        {
            get => _MemberCaptainIds;
            set => _MemberCaptainIds = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private string _GroupKey = String.Empty;
        private string _TenantId = String.Empty;
        private string _HostId = String.Empty;
        private List<string> _MemberCaptainIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageCaptureGroup()
        {
        }

        #endregion
    }
}