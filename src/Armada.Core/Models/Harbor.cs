namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A registered Harbor: a detached host-side runner that connects to the Admiral over an authenticated
    /// link and executes host operations (agent processes, git, worktrees) on the machine where the
    /// developer's repositories and tool logins live. One Admiral may drive many Harbors across machines;
    /// a mission's dock is pinned to the Harbor that provisioned it (dock affinity).
    /// </summary>
    public class Harbor
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (hbr_ prefix).
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
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Human-facing Harbor name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Capabilities advertised at handshake (available runtimes and host tools).
        /// </summary>
        public List<HarborCapability> Capabilities
        {
            get => _Capabilities;
            set => _Capabilities = value ?? new List<HarborCapability>();
        }

        /// <summary>
        /// Connection state as seen by the Admiral.
        /// </summary>
        public HarborConnectionStatusEnum ConnectionStatus { get; set; } = HarborConnectionStatusEnum.Unknown;

        /// <summary>
        /// Maximum number of concurrent jobs the Harbor will accept. Clamped to a minimum of 1; defaults
        /// to 4.
        /// </summary>
        public int MaxConcurrentJobs
        {
            get => _MaxConcurrentJobs;
            set => _MaxConcurrentJobs = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Whether the Harbor is enabled for routing. A disabled Harbor keeps its docks but receives no new
        /// missions.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Protocol version reported at handshake, or null if never connected.
        /// </summary>
        public string? ProtocolVersion { get; set; } = null;

        /// <summary>
        /// Operating-system platform reported at handshake (e.g. "Windows", "Linux", "macOS").
        /// </summary>
        public string? OsPlatform { get; set; } = null;

        /// <summary>
        /// Processor architecture reported at handshake (e.g. "X64", "Arm64").
        /// </summary>
        public string? Architecture { get; set; } = null;

        /// <summary>
        /// When the Harbor last sent a heartbeat or message (UTC), or null if never.
        /// </summary>
        public DateTime? LastSeenUtc { get; set; } = null;

        /// <summary>
        /// When the Harbor last established a link (UTC), or null if never.
        /// </summary>
        public DateTime? LastConnectedUtc { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.HarborIdPrefix, 24);
        private string _Name = "New Harbor";
        private List<HarborCapability> _Capabilities = new List<HarborCapability>();
        private int _MaxConcurrentJobs = 4;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Harbor()
        {
        }

        #endregion
    }
}
