namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// A reusable, named action that can be applied across many vessels. A Command action runs
    /// <see cref="CommandText"/> in each vessel's working directory; a Mission action dispatches one voyage per vessel
    /// rendered from <see cref="PromptTemplate"/>. Built-in actions are seeded per tenant as ordinary rows; deleting a
    /// built-in action soft-deletes it (<see cref="Active"/> = false) so it is not re-seeded.
    /// </summary>
    public class FleetAction
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (fac_ prefix). Defaults to a new identifier; a create backfills an empty value.
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? String.Empty;
        }

        /// <summary>
        /// Owning tenant identifier. Every row carries a tenant; reads and enumerations are scoped by it.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Identifier of the user who created the action.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Display name. Never null.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Action kind. Defaults to Command.
        /// </summary>
        public FleetActionKindEnum Kind { get; set; } = FleetActionKindEnum.Command;

        /// <summary>
        /// Shell command template (Command kind only).
        /// </summary>
        public string? CommandText { get; set; } = null;

        /// <summary>
        /// Mission prompt template (Mission kind only).
        /// </summary>
        public string? PromptTemplate { get; set; } = null;

        /// <summary>
        /// Optional pipeline identifier used when dispatching (Mission kind only).
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Optional persona name used when dispatching (Mission kind only).
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Per-target command timeout in seconds (Command kind only). Default 300, minimum 5, maximum 7200; out-of-range values are clamped.
        /// </summary>
        public int TimeoutSeconds
        {
            get => _TimeoutSeconds;
            set => _TimeoutSeconds = value < 5 ? 5 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Default number of targets processed at once. Default 4, minimum 1, maximum 32; out-of-range values are clamped.
        /// </summary>
        public int DefaultConcurrency
        {
            get => _DefaultConcurrency;
            set => _DefaultConcurrency = value < 1 ? 1 : (value > 32 ? 32 : value);
        }

        /// <summary>
        /// Whether a target with a dirty working tree is skipped. Default true.
        /// </summary>
        public bool RequiresCleanWorkingTree { get; set; } = true;

        /// <summary>
        /// Whether this action was seeded as a built-in.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Stable key identifying the built-in this row was seeded from, or null for user-defined actions.
        /// </summary>
        public string? BuiltInKey { get; set; } = null;

        /// <summary>
        /// Whether the action is active. False for soft-deleted built-ins. Default true.
        /// </summary>
        public bool Active { get; set; } = true;

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.FleetActionIdPrefix, 24);
        private string _Name = String.Empty;
        private int _TimeoutSeconds = 300;
        private int _DefaultConcurrency = 4;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetAction()
        {
        }

        #endregion
    }
}
