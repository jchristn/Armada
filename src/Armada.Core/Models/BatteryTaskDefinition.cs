namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// One battery task: the mission prompt, the persona it tests and how it is graded.
    /// </summary>
    public class BatteryTaskDefinition
    {
        #region Public-Members

        /// <summary>
        /// Task identifier, unique in the pack.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Title; also the mission title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Persona the task tests (Worker, Test Engineer, Judge, Architect, Linter, Recorder, ...).
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Optional difficulty label.
        /// </summary>
        public string? Difficulty { get; set; } = null;

        /// <summary>
        /// Mission description.
        /// </summary>
        public string? Prompt { get; set; } = null;

        /// <summary>
        /// Files, relative to the pack directory, appended to the mission description in fenced blocks.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Attachments
        {
            get => _Attachments;
            set => _Attachments = value ?? new List<string>();
        }

        /// <summary>
        /// Directory, relative to the pack directory, copied over the fixture when it is reset for this task
        /// (committed, so the task starts from it), or null.
        /// </summary>
        public string? Setup { get; set; } = null;

        /// <summary>
        /// Names of the toolchain entries the task needs; null means every entry of the pack, an empty list none.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string>? Toolchain { get; set; } = null;

        /// <summary>
        /// Minutes to wait for the mission, or null for the evaluation default.
        /// </summary>
        public int? TimeoutMinutes { get; set; } = null;

        /// <summary>
        /// How the task is graded.
        /// </summary>
        public BatteryGraderDefinition? Grader { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _Attachments = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryTaskDefinition()
        {
        }

        #endregion
    }
}
