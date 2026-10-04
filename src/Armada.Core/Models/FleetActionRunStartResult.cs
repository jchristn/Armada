namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Response for a started fleet action run. Never carries target output.
    /// </summary>
    public class FleetActionRunStartResult
    {
        #region Public-Members

        /// <summary>
        /// Run identifier (far_ prefix).
        /// </summary>
        public string RunId { get; set; } = String.Empty;

        /// <summary>
        /// Source action identifier, or null for an ad hoc run.
        /// </summary>
        public string? ActionId { get; set; } = null;

        /// <summary>
        /// Run kind.
        /// </summary>
        public FleetActionKindEnum Kind { get; set; } = FleetActionKindEnum.Command;

        /// <summary>
        /// Run status at the time of the response (normally Pending).
        /// </summary>
        public FleetActionRunStatusEnum Status { get; set; } = FleetActionRunStatusEnum.Pending;

        /// <summary>
        /// Number of targets.
        /// </summary>
        public int TargetCount { get; set; } = 0;

        /// <summary>
        /// Effective concurrency.
        /// </summary>
        public int Concurrency { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionRunStartResult()
        {
        }

        /// <summary>
        /// Build from a run.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="run"/> is null.</exception>
        public static FleetActionRunStartResult FromRun(FleetActionRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            return new FleetActionRunStartResult
            {
                RunId = run.Id,
                ActionId = run.ActionId,
                Kind = run.Kind,
                Status = run.Status,
                TargetCount = run.TargetCount,
                Concurrency = run.Concurrency
            };
        }

        #endregion
    }
}
