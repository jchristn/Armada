namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// Body for creating or updating a fleet action, and the inline definition of an ad hoc run. On create, omitted
    /// values take their defaults; on update, only supplied (non-null) values change.
    /// </summary>
    public class FleetActionUpsertRequest
    {
        #region Public-Members

        /// <summary>
        /// Display name. Required on create (1 to 200 characters after trimming).
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Action kind. Defaults to Command on create.
        /// </summary>
        public FleetActionKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Shell command template. Required for Command kind.
        /// </summary>
        public string? CommandText { get; set; } = null;

        /// <summary>
        /// Prompt template. Required for Mission kind.
        /// </summary>
        public string? PromptTemplate { get; set; } = null;

        /// <summary>
        /// Optional pipeline identifier (Mission kind). An empty string clears it on update.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Optional persona name (Mission kind). Stored with the action and run snapshot. An empty string clears it on update.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Per-target timeout in seconds (Command kind). Defaults to FleetActions.DefaultTimeoutSeconds; clamped 5 to 7200.
        /// </summary>
        public int? TimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Default concurrency. Default 4; clamped 1 to 32.
        /// </summary>
        public int? DefaultConcurrency { get; set; } = null;

        /// <summary>
        /// Whether targets with a dirty working tree are skipped (Command kind). Defaults to true for Command and
        /// false for Mission on create.
        /// </summary>
        public bool? RequiresCleanWorkingTree { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionUpsertRequest()
        {
        }

        #endregion
    }
}
