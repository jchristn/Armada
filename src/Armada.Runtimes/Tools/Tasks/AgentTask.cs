namespace Armada.Runtimes.Tools.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Runtimes.Tools.Tasks;

    /// <summary>
    /// One node in a job's task plan: a short, model-authored unit of work with a stable id, a status,
    /// and its dependencies on other tasks in the same plan. Tasks are a tracking primitive that lives
    /// inside a job; they are never a bare <see cref="System.Threading.Tasks.Task"/>.
    /// </summary>
    public class AgentTask
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private AgentTaskStatusEnum _Status = AgentTaskStatusEnum.Pending;
        private List<string> _DependsOn = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentTask"/> class with default values.
        /// </summary>
        public AgentTask()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The stable, model-assigned identifier for this task (for example <c>"t1"</c>). Unique within
        /// a plan. Never null; an empty id fails plan validation.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// A short, imperative label for the task (for example <c>"Add IRequestHistoryMethods"</c>).
        /// Never null; an empty title fails plan validation.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title
        {
            get => _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// The current lifecycle status of the task. Defaults to <see cref="AgentTaskStatusEnum.Pending"/>.
        /// </summary>
        [JsonPropertyName("status")]
        public AgentTaskStatusEnum Status
        {
            get => _Status;
            set => _Status = value;
        }

        /// <summary>
        /// The ids of tasks that must reach <see cref="AgentTaskStatusEnum.Completed"/> before this task
        /// is ready to start. Never null; empty means the task has no prerequisites. These edges form the
        /// plan's dependency graph and must not contain a cycle.
        /// </summary>
        [JsonPropertyName("dependsOn")]
        public List<string> DependsOn
        {
            get => _DependsOn;
            set => _DependsOn = value ?? new List<string>();
        }

        /// <summary>
        /// An optional running annotation on the task — a blocker, a decision, or a result. Null when the
        /// task carries no note. A note is required when the status is <see cref="AgentTaskStatusEnum.Failed"/>.
        /// </summary>
        [JsonPropertyName("note")]
        public string? Note { get; set; }

        /// <summary>
        /// The UTC time the task was created (added to a plan).
        /// </summary>
        [JsonPropertyName("createdUtc")]
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The UTC time the task first entered <see cref="AgentTaskStatusEnum.InProgress"/>, or null if
        /// it has not started.
        /// </summary>
        [JsonPropertyName("startedUtc")]
        public DateTime? StartedUtc { get; set; }

        /// <summary>
        /// The UTC time the task reached a terminal status (completed, failed, or skipped), or null if it
        /// has not finished.
        /// </summary>
        [JsonPropertyName("completedUtc")]
        public DateTime? CompletedUtc { get; set; }

        /// <summary>
        /// The elapsed wall-clock duration in milliseconds between <see cref="StartedUtc"/> and
        /// <see cref="CompletedUtc"/>, filled when the task reaches a terminal status and both stamps are
        /// present; otherwise null.
        /// </summary>
        [JsonPropertyName("durationMs")]
        public long? DurationMs { get; set; }

        /// <summary>
        /// The name of the endpoint this task should run under when dispatched as its own job in
        /// orchestrated mode, or null to inherit the parent job's endpoint. Advisory in tracking mode.
        /// </summary>
        [JsonPropertyName("assignedEndpointName")]
        public string? AssignedEndpointName { get; set; }

        /// <summary>
        /// The reason the task failed, set when <see cref="Status"/> is
        /// <see cref="AgentTaskStatusEnum.Failed"/>; otherwise null.
        /// </summary>
        [JsonPropertyName("failureMessage")]
        public string? FailureMessage { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a deep copy of this task so callers never hold a reference into live mutable plan state.
        /// </summary>
        /// <returns>A new <see cref="AgentTask"/> with the same field values and an independent
        /// <see cref="DependsOn"/> list.</returns>
        public AgentTask Clone()
        {
            return new AgentTask
            {
                Id = _Id,
                Title = _Title,
                Status = _Status,
                DependsOn = new List<string>(_DependsOn),
                Note = Note,
                CreatedUtc = CreatedUtc,
                StartedUtc = StartedUtc,
                CompletedUtc = CompletedUtc,
                DurationMs = DurationMs,
                AssignedEndpointName = AssignedEndpointName,
                FailureMessage = FailureMessage
            };
        }

        #endregion
    }
}
