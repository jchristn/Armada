namespace Armada.Core.Services
{
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Options a Full Evaluate passes to a battery run: a persona filter, its own token budget, a cancellation token and
    /// whether the caller already holds the evaluation concurrency slot for the inference host. The service writes the
    /// tokens spent and why it stopped early back into the instance.
    /// </summary>
    public class BatteryRunOptions
    {
        #region Public-Members

        /// <summary>
        /// Personas to run, or null for every task of the packs. Tasks of other personas are not dispatched.
        /// </summary>
        public List<string>? Personas { get; set; } = null;

        /// <summary>
        /// Estimated tokens the run may spend (prompt plus output of each attempt); 0 means no limit.
        /// </summary>
        public long TokenBudget { get; set; } = 0;

        /// <summary>
        /// Stops the run: no further task is dispatched and the open mission is cancelled (releasing its dock and captain).
        /// </summary>
        public CancellationToken Cancellation { get; set; } = CancellationToken.None;

        /// <summary>
        /// Reason recorded when <see cref="Cancellation"/> fires, or null for "cancelled".
        /// </summary>
        public string? StopReason { get; set; } = null;

        /// <summary>
        /// True when the caller already holds the evaluation concurrency slot for the run's inference host, so the run
        /// must not wait for another (a Full Evaluate holds one slot for its probes and its battery).
        /// </summary>
        public bool HoldsEvaluationSlot { get; set; } = false;

        /// <summary>
        /// Estimated tokens the run spent (written by the service).
        /// </summary>
        public long TokensUsed { get; set; } = 0;

        /// <summary>
        /// Why the run stopped before its last task (cancelled, timed out, budget spent), or null (written by the service).
        /// </summary>
        public string? StoppedEarlyReason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatteryRunOptions()
        {
        }

        #endregion
    }
}
