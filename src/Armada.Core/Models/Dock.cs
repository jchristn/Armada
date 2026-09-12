namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A git worktree provisioned for a captain.
    /// </summary>
    public class Dock
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
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
        /// Vessel identifier this dock is for.
        /// </summary>
        public string VesselId
        {
            get => _VesselId;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(VesselId));
                _VesselId = value;
            }
        }

        /// <summary>
        /// Captain identifier currently using this dock.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Identifier of the Harbor (host runner) that owns this dock, or null when the dock is on the
        /// Admiral's own host (Local mode). A dock's worktree lives on exactly one host, so this pins the
        /// mission's later host operations to that Harbor (dock affinity).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Local filesystem path to the worktree.
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// Branch name checked out in this worktree.
        /// </summary>
        public string? BranchName { get; set; } = null;

        /// <summary>
        /// Whether the dock is active and usable.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Authoritative lifecycle state of the dock. Occupancy is tracked here rather than
        /// inferred from captain/mission rows, so a stuck or orphaned dock can be detected and
        /// reclaimed deterministically.
        /// </summary>
        public DockStateEnum State { get; set; } = DockStateEnum.Available;

        /// <summary>
        /// UTC time at which the current lease expires. A leased dock whose lease has elapsed
        /// without renewal is eligible for reclamation even in a multi-instance deployment.
        /// Null when the dock is not leased.
        /// </summary>
        public DateTime? LeaseExpiresUtc { get; set; } = null;

        /// <summary>
        /// Opaque token identifying the current lease holder (typically the owning captain plus
        /// a generation stamp). Used for compare-and-swap lease acquisition and renewal so two
        /// instances cannot both claim the same dock. Null when the dock is not leased.
        /// </summary>
        public string? OwnerToken { get; set; } = null;

        /// <summary>
        /// Resolved git anchors captured at dock provisioning, serialized as JSON (start commit, target
        /// branch, working branch, recent-commit and subject-term summaries). This is a documented,
        /// intentional raw-JSON snapshot for the dashboard and for a resuming captain -- not a general
        /// data blob. Null when anchors were not resolved.
        /// </summary>
        public string? GitAnchorsJson { get; set; } = null;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.DockIdPrefix, 24);
        private string _VesselId = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Dock()
        {
        }

        /// <summary>
        /// Instantiate with vessel identifier.
        /// </summary>
        /// <param name="vesselId">Vessel identifier.</param>
        public Dock(string vesselId)
        {
            VesselId = vesselId;
        }

        #endregion
    }
}
