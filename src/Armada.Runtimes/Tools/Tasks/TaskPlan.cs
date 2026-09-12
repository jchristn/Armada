namespace Armada.Runtimes.Tools.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Tools.Tasks;

    /// <summary>
    /// A job's plan of tasks: the ordered set of <see cref="AgentTask"/> the model authors and advances
    /// as it works, plus a monotonic <see cref="Version"/> that increments on every mutation.
    /// <para>
    /// Thread safety: all reads and writes are serialized on a private lock. Every accessor that returns
    /// tasks returns deep copies (<see cref="AgentTask.Clone"/>), so a caller never holds a reference into
    /// live mutable state and may read the plan on one thread while a worker mutates it on another.
    /// </para>
    /// </summary>
    public class TaskPlan
    {
        #region Private-Members

        private readonly object _SyncRoot = new object();
        private List<AgentTask> _Tasks = new List<AgentTask>();
        private int _Version;
        private DateTime? _CreatedUtc;
        private TaskPlanChangeKindEnum _LastChangeKind = TaskPlanChangeKindEnum.PlanCreated;
        private string? _LastChangedTaskId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new, empty task plan.
        /// </summary>
        public TaskPlan()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// A monotonic counter incremented under the lock on every mutation. Consumers compare it to
        /// decide whether the plan changed since they last observed it.
        /// </summary>
        public int Version
        {
            get { lock (_SyncRoot) { return _Version; } }
        }

        /// <summary>
        /// The UTC time the plan was first populated, or null while the plan is empty.
        /// </summary>
        public DateTime? CreatedUtc
        {
            get { lock (_SyncRoot) { return _CreatedUtc; } }
        }

        /// <summary>
        /// The kind of the most recent change applied to the plan. Paired with
        /// <see cref="Version"/> so an observer can build a change event without racing the mutation.
        /// </summary>
        public TaskPlanChangeKindEnum LastChangeKind
        {
            get { lock (_SyncRoot) { return _LastChangeKind; } }
        }

        /// <summary>
        /// The id of the task touched by the most recent change, or null when the last change was a
        /// whole-plan change (created, replaced, cleared).
        /// </summary>
        public string? LastChangedTaskId
        {
            get { lock (_SyncRoot) { return _LastChangedTaskId; } }
        }

        /// <summary>
        /// Whether the plan currently has no tasks.
        /// </summary>
        public bool IsEmpty
        {
            get { lock (_SyncRoot) { return _Tasks.Count == 0; } }
        }

        /// <summary>
        /// The number of tasks in the plan.
        /// </summary>
        public int TotalCount
        {
            get { lock (_SyncRoot) { return _Tasks.Count; } }
        }

        /// <summary>
        /// The number of tasks that have reached <see cref="AgentTaskStatusEnum.Completed"/>.
        /// </summary>
        public int CompletedCount
        {
            get
            {
                lock (_SyncRoot)
                {
                    int count = 0;
                    foreach (AgentTask task in _Tasks)
                    {
                        if (task.Status == AgentTaskStatusEnum.Completed) count++;
                    }
                    return count;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replaces the plan with a validated set of tasks. An empty or null set clears the plan.
        /// </summary>
        /// <param name="tasks">The new tasks. Each is deep-copied into the plan.</param>
        /// <returns>The change that occurred: <see cref="TaskPlanChangeKindEnum.PlanCreated"/>,
        /// <see cref="TaskPlanChangeKindEnum.PlanReplaced"/>, or <see cref="TaskPlanChangeKindEnum.PlanCleared"/>.</returns>
        /// <exception cref="TaskPlanValidationException">Thrown when the proposed tasks fail validation.</exception>
        public TaskPlanChangeKindEnum SetPlan(IReadOnlyList<AgentTask>? tasks)
        {
            TaskPlanValidationResult validation = TaskPlanValidator.Validate(tasks);
            if (!validation.IsValid)
            {
                throw new TaskPlanValidationException("The proposed task plan is invalid.", validation.Problems);
            }

            lock (_SyncRoot)
            {
                bool hadTasks = _Tasks.Count > 0;

                if (tasks == null || tasks.Count == 0)
                {
                    _Tasks = new List<AgentTask>();
                    _CreatedUtc = null;
                    _Version++;
                    _LastChangeKind = TaskPlanChangeKindEnum.PlanCleared;
                    _LastChangedTaskId = null;
                    return TaskPlanChangeKindEnum.PlanCleared;
                }

                List<AgentTask> copied = new List<AgentTask>(tasks.Count);
                foreach (AgentTask task in tasks)
                {
                    copied.Add(task.Clone());
                }

                _Tasks = copied;
                if (_CreatedUtc == null) _CreatedUtc = DateTime.UtcNow;
                _Version++;
                _LastChangeKind = hadTasks ? TaskPlanChangeKindEnum.PlanReplaced : TaskPlanChangeKindEnum.PlanCreated;
                _LastChangedTaskId = null;
                return _LastChangeKind;
            }
        }

        /// <summary>
        /// Advances a single task's status and optionally its note, stamping timing on the appropriate
        /// transitions and incrementing <see cref="Version"/> when the task is found.
        /// </summary>
        /// <param name="id">The id of the task to update. Must not be null.</param>
        /// <param name="status">The new status.</param>
        /// <param name="note">An optional note. Required (non-empty) when <paramref name="status"/> is
        /// <see cref="AgentTaskStatusEnum.Failed"/>.</param>
        /// <param name="changeKind">On success, the kind of change that occurred.</param>
        /// <returns>True when a task with the given id was found and updated; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="status"/> is
        /// <see cref="AgentTaskStatusEnum.Failed"/> and <paramref name="note"/> is null or whitespace.</exception>
        public bool TryUpdateTask(string id, AgentTaskStatusEnum status, string? note, out TaskPlanChangeKindEnum changeKind)
        {
            ArgumentNullException.ThrowIfNull(id);
            if (status == AgentTaskStatusEnum.Failed && string.IsNullOrWhiteSpace(note))
            {
                throw new ArgumentException("A note is required when marking a task as failed.", nameof(note));
            }

            changeKind = TaskPlanChangeKindEnum.TaskStatusChanged;

            lock (_SyncRoot)
            {
                AgentTask? target = null;
                foreach (AgentTask task in _Tasks)
                {
                    if (string.Equals(task.Id, id, StringComparison.Ordinal))
                    {
                        target = task;
                        break;
                    }
                }

                if (target == null) return false;

                AgentTaskStatusEnum previous = target.Status;
                bool statusChanged = previous != status;

                if (status == AgentTaskStatusEnum.InProgress && target.StartedUtc == null)
                {
                    target.StartedUtc = DateTime.UtcNow;
                }

                if (IsTerminal(status))
                {
                    target.CompletedUtc = DateTime.UtcNow;
                    if (target.StartedUtc != null)
                    {
                        target.DurationMs = (long)(target.CompletedUtc.Value - target.StartedUtc.Value).TotalMilliseconds;
                    }
                }

                target.Status = status;
                target.FailureMessage = status == AgentTaskStatusEnum.Failed ? note : null;
                if (note != null) target.Note = note;

                changeKind = statusChanged ? TaskPlanChangeKindEnum.TaskStatusChanged : TaskPlanChangeKindEnum.TaskNoteUpdated;
                _Version++;
                _LastChangeKind = changeKind;
                _LastChangedTaskId = target.Id;
                return true;
            }
        }

        /// <summary>
        /// Returns a deep, immutable copy of the current tasks in plan order, safe to hand to events,
        /// rendering, and persistence.
        /// </summary>
        /// <returns>A new list of cloned tasks; never null.</returns>
        public IReadOnlyList<AgentTask> Snapshot()
        {
            lock (_SyncRoot)
            {
                List<AgentTask> copy = new List<AgentTask>(_Tasks.Count);
                foreach (AgentTask task in _Tasks)
                {
                    copy.Add(task.Clone());
                }
                return copy;
            }
        }

        /// <summary>
        /// Returns the tasks that are ready to start: those with status
        /// <see cref="AgentTaskStatusEnum.Pending"/> whose every dependency is
        /// <see cref="AgentTaskStatusEnum.Completed"/>.
        /// </summary>
        /// <returns>A new list of cloned ready tasks; never null.</returns>
        public IReadOnlyList<AgentTask> ReadyTasks()
        {
            lock (_SyncRoot)
            {
                return ComputeReadyTasksNoLock();
            }
        }

        /// <summary>
        /// Asynchronous variant of <see cref="ReadyTasks"/>. The computation is in-memory; the token is
        /// honored before the snapshot is taken.
        /// </summary>
        /// <param name="token">A token to observe for cancellation.</param>
        /// <returns>A task producing the ready tasks.</returns>
        public Task<IReadOnlyList<AgentTask>> ReadyTasksAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ReadyTasks());
        }

        /// <summary>
        /// Returns the tasks currently in the given status, in plan order.
        /// </summary>
        /// <param name="status">The status to filter by.</param>
        /// <returns>A new list of cloned matching tasks; never null.</returns>
        public IReadOnlyList<AgentTask> TasksInStatus(AgentTaskStatusEnum status)
        {
            lock (_SyncRoot)
            {
                List<AgentTask> matches = new List<AgentTask>();
                foreach (AgentTask task in _Tasks)
                {
                    if (task.Status == status) matches.Add(task.Clone());
                }
                return matches;
            }
        }

        /// <summary>
        /// Asynchronous variant of <see cref="TasksInStatus"/>. The computation is in-memory; the token is
        /// honored before the snapshot is taken.
        /// </summary>
        /// <param name="status">The status to filter by.</param>
        /// <param name="token">A token to observe for cancellation.</param>
        /// <returns>A task producing the matching tasks.</returns>
        public Task<IReadOnlyList<AgentTask>> TasksInStatusAsync(AgentTaskStatusEnum status, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(TasksInStatus(status));
        }

        #endregion

        #region Private-Methods

        private IReadOnlyList<AgentTask> ComputeReadyTasksNoLock()
        {
            HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (AgentTask task in _Tasks)
            {
                if (task.Status == AgentTaskStatusEnum.Completed) completed.Add(task.Id);
            }

            List<AgentTask> ready = new List<AgentTask>();
            foreach (AgentTask task in _Tasks)
            {
                if (task.Status != AgentTaskStatusEnum.Pending) continue;

                bool dependenciesMet = true;
                foreach (string dependency in task.DependsOn)
                {
                    if (!completed.Contains(dependency))
                    {
                        dependenciesMet = false;
                        break;
                    }
                }

                if (dependenciesMet) ready.Add(task.Clone());
            }

            return ready;
        }

        private static bool IsTerminal(AgentTaskStatusEnum status)
        {
            return status == AgentTaskStatusEnum.Completed
                || status == AgentTaskStatusEnum.Failed
                || status == AgentTaskStatusEnum.Skipped;
        }

        #endregion
    }
}
